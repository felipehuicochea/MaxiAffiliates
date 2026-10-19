using System.Text.RegularExpressions;
using BTCPayServer.Plugins.MaxiAffiliates.Data;
using BTCPayServer.Plugins.MaxiAffiliates.Services;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using Npgsql;

namespace BTCPayServer.Plugins.MaxiAffiliates.Tests;

public class TransitionTests
{
    [Theory]
    [InlineData(AffiliateStatus.Pending, AffiliateAction.Approve, AffiliateStatus.Active)]
    [InlineData(AffiliateStatus.Active, AffiliateAction.Suspend, AffiliateStatus.Suspended)]
    [InlineData(AffiliateStatus.Suspended, AffiliateAction.Reactivate, AffiliateStatus.Active)]
    [InlineData(AffiliateStatus.Pending, AffiliateAction.Ban, AffiliateStatus.Banned)]
    [InlineData(AffiliateStatus.Active, AffiliateAction.Ban, AffiliateStatus.Banned)]
    [InlineData(AffiliateStatus.Suspended, AffiliateAction.Ban, AffiliateStatus.Banned)]
    public void Allowed(AffiliateStatus from, AffiliateAction a, AffiliateStatus to) =>
        Assert.Equal(to, AffiliateService.Transition(from, a));

    [Theory]
    [InlineData(AffiliateStatus.Banned, AffiliateAction.Approve)]
    [InlineData(AffiliateStatus.Banned, AffiliateAction.Reactivate)]
    [InlineData(AffiliateStatus.Banned, AffiliateAction.Suspend)]
    [InlineData(AffiliateStatus.Banned, AffiliateAction.Ban)]
    [InlineData(AffiliateStatus.Pending, AffiliateAction.Suspend)]
    [InlineData(AffiliateStatus.Pending, AffiliateAction.Reactivate)]
    [InlineData(AffiliateStatus.Active, AffiliateAction.Approve)]
    [InlineData(AffiliateStatus.Suspended, AffiliateAction.Approve)]
    public void Rejected(AffiliateStatus from, AffiliateAction a) => Assert.Null(AffiliateService.Transition(from, a));

    [Fact]
    public void CodesAreUnambiguous()
    {
        for (var i = 0; i < 200; i++)
            Assert.Matches("^[A-HJ-NP-Z2-9]{8}$", AffiliateService.GenerateCode());
    }
}

public class PayoutDestinationTests
{
    static readonly Network Reg = Network.RegTest;
    [Theory]
    [InlineData("Alice@Wallet.Example", "alice@wallet.example")]
    [InlineData("lnurl1dp68gurn8ghj7um9wfmxjcm99e3k7mf0v9cxj0m385ekvcenxc6r2c35xvukxefcv5mkvv34x5ekzd3ev56nyd3hxqurzepexejxxepnxscrvwfnv9nxzcn9xq6xyefhvgcxxcmyxymnserxfq5fns", null)]
    public void LightningOk(string input, string? expected)
    {
        var r = PayoutDestinationValidator.Normalize(PayoutMethodKind.Lightning, input, Reg);
        Assert.NotNull(r);
        if (expected != null) Assert.Equal(expected, r);
    }
    [Theory]
    [InlineData("not-an-address")] [InlineData("@wallet.com")] [InlineData("alice@")] [InlineData("")] [InlineData("bcrt1qw508d6qejxtdg4y5r3zarvary0c5xw7kygt080")]
    public void LightningBad(string input) => Assert.Null(PayoutDestinationValidator.Normalize(PayoutMethodKind.Lightning, input, Reg));

    [Fact]
    public void OnChainRegtestOk()
    {
        var addr = new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, Reg).ToString();
        Assert.Equal(addr, PayoutDestinationValidator.Normalize(PayoutMethodKind.OnChain, " " + addr + " ", Reg));
    }
    [Fact]
    public void OnChainWrongNetworkRejected()
    {
        var mainnet = new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, Network.Main).ToString();
        Assert.Null(PayoutDestinationValidator.Normalize(PayoutMethodKind.OnChain, mainnet, Reg));
        Assert.Null(PayoutDestinationValidator.Normalize(PayoutMethodKind.OnChain, "alice@wallet.com", Reg));
    }
}

public class LocalizationTests
{
    static string SrcDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "src", "BTCPayServer.Plugins.MaxiAffiliates"))) d = d.Parent;
        return Path.Combine(d!.FullName, "src", "BTCPayServer.Plugins.MaxiAffiliates");
    }

    [Fact]
    public void EveryUiStringIsInEnglishResource()
    {
        var keys = MaxiAffiliatesTranslationProvider.Load("en").Keys.ToHashSet();
        var used = new HashSet<string>();
        foreach (var f in Directory.EnumerateFiles(SrcDir(), "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cshtml") || f.EndsWith(".cs")) && !f.Contains("/obj/") && !f.Contains("/bin/") && !f.Contains("/Migrations/")))
        {
            var t = File.ReadAllText(f);
            foreach (Match m in Regex.Matches(t, @"text-translate=""true""[^>]*>([^<@]+)<")) used.Add(m.Groups[1].Value.Trim());
            foreach (Match m in Regex.Matches(t, @"(?:StringLocalizer|localizer)\[""([^""]+)""")) used.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(t, @"new PermissionDisplay\(""([^""]+)"",\s*""([^""]+)""\)")) { used.Add(m.Groups[1].Value); used.Add(m.Groups[2].Value); }
            foreach (Match m in Regex.Matches(t, @"(?:ErrorKey|, ""|: "")((?:Enter|The affiliate|This payout|An affiliate)[^""]+)""")) used.Add(m.Groups[1].Value);
        }
        // dynamic keys: enum names rendered through the localizer
        foreach (var n in Enum.GetNames<AffiliateStatus>().Concat(Enum.GetNames<AffiliateAction>())) used.Add(n);
        Assert.True(used.Count > 30, $"only {used.Count} strings found; scanner broken?");
        var missing = used.Where(u => !keys.Contains(u)).OrderBy(x => x).ToArray();
        Assert.True(missing.Length == 0, "Missing from en.json: " + string.Join(" | ", missing));
    }
}

/// <summary>Acceptance tests for M2 service behaviour against the dev Postgres.</summary>
[Trait("Category", "Integration")]
public class AffiliateServiceIntegrationTests : IAsyncLifetime
{
    const string Server = "Host=127.0.0.1;Port=39372;User ID=postgres";
    readonly string _db = "maxiaff_m2_" + Guid.NewGuid().ToString("N")[..8];
    DbContextOptions<MaxiAffiliatesDbContext> _opts = null!;
    AffiliateService _svc = null!;
    bool _available;

    class Factory(DbContextOptions<MaxiAffiliatesDbContext> o) : IDbContextFactory<MaxiAffiliatesDbContext>
    { public MaxiAffiliatesDbContext CreateDbContext() => new(o); }

    public async ValueTask InitializeAsync()
    {
        try
        {
            await using var admin = new NpgsqlConnection(Server + ";Database=postgres");
            await admin.OpenAsync();
            await new NpgsqlCommand($"CREATE DATABASE \"{_db}\"", admin).ExecuteNonQueryAsync();
            _available = true;
        }
        catch (NpgsqlException) { return; }
        catch (System.Net.Sockets.SocketException) { return; }
        _opts = new DbContextOptionsBuilder<MaxiAffiliatesDbContext>().UseNpgsql($"{Server};Database={_db}").Options;
        await using (var ctx = new MaxiAffiliatesDbContext(_opts)) await ctx.Database.MigrateAsync();
        _svc = new AffiliateService(new Factory(_opts), TimeProvider.System);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_available) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(Server + ";Database=postgres");
        await admin.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_db}\" WITH (FORCE)", admin).ExecuteNonQueryAsync();
    }

    void RequireDb() { if (!_available) Assert.Skip("dev Postgres not running"); }
    static SignupRequest Ln(string store, string email) => new(store, email, "Ana", PayoutMethodKind.Lightning, "ana@wallet.example");

    [Fact]
    public async Task SignupCreatesPendingAffiliateWithCode()
    {
        RequireDb();
        var r = await _svc.Signup(Ln("s1", " Ana@Example.COM "), Network.RegTest);
        Assert.True(r.Success);
        Assert.Equal(AffiliateStatus.Pending, r.Affiliate!.Status);
        Assert.Equal("ana@example.com", r.Affiliate.Email);
        Assert.Matches("^[A-Z2-9]{8}$", r.Affiliate.Code);
        Assert.Null(await _svc.FindActiveByCode("s1", r.Affiliate.Code)); // pending links don't resolve
    }

    [Fact]
    public async Task DuplicateEmailRejectedPerStoreOnly()
    {
        RequireDb();
        Assert.True((await _svc.Signup(Ln("s2", "dup@example.com"), Network.RegTest)).Success);
        var again = await _svc.Signup(Ln("s2", "DUP@example.com"), Network.RegTest);
        Assert.False(again.Success);
        Assert.Equal("Email", again.ErrorField);
        Assert.True((await _svc.Signup(Ln("s3", "dup@example.com"), Network.RegTest)).Success);
    }

    [Fact]
    public async Task InvalidInputsRejected()
    {
        RequireDb();
        Assert.Equal("Email", (await _svc.Signup(Ln("s4", "nope"), Network.RegTest)).ErrorField);
        var bad = await _svc.Signup(new SignupRequest("s4", "x@example.com", null, PayoutMethodKind.OnChain, "bc1qnotvalid"), Network.RegTest);
        Assert.Equal("PayoutDestination", bad.ErrorField);
    }

    [Fact]
    public async Task DisabledMethodAndAutoApproveRespectSettings()
    {
        RequireDb();
        await using (var ctx = new MaxiAffiliatesDbContext(_opts))
        {
            ctx.StoreSettings.Add(new AffiliateStoreSettings { StoreId = "s5", OnChainEnabled = false, AutoApproveAffiliates = true, UpdatedAt = DateTimeOffset.UtcNow });
            await ctx.SaveChangesAsync();
        }
        var addr = new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, Network.RegTest).ToString();
        var oc = await _svc.Signup(new SignupRequest("s5", "oc@example.com", null, PayoutMethodKind.OnChain, addr), Network.RegTest);
        Assert.Equal("PayoutMethod", oc.ErrorField);
        var ln = await _svc.Signup(Ln("s5", "ln@example.com"), Network.RegTest);
        Assert.Equal(AffiliateStatus.Active, ln.Affiliate!.Status);
    }

    [Fact]
    public async Task ApproveSuspendReactivateBanLifecycle()
    {
        RequireDb();
        var a = (await _svc.Signup(Ln("s6", "life@example.com"), Network.RegTest)).Affiliate!;
        Assert.True((await _svc.Apply("s6", a.Id, AffiliateAction.Approve)).ok);
        Assert.NotNull(await _svc.FindActiveByCode("s6", a.Code.ToLowerInvariant()));
        Assert.True((await _svc.Apply("s6", a.Id, AffiliateAction.Suspend)).ok);
        Assert.Null(await _svc.FindActiveByCode("s6", a.Code));
        Assert.True((await _svc.Apply("s6", a.Id, AffiliateAction.Reactivate)).ok);
        Assert.True((await _svc.Apply("s6", a.Id, AffiliateAction.Ban)).ok);
        Assert.Null(await _svc.FindActiveByCode("s6", a.Code));
        var (ok, after) = await _svc.Apply("s6", a.Id, AffiliateAction.Reactivate);
        Assert.False(ok);
        Assert.Equal(AffiliateStatus.Banned, after!.Status);
    }

    [Fact]
    public async Task ActionsAreStoreScoped()
    {
        RequireDb();
        var a = (await _svc.Signup(Ln("s7", "scope@example.com"), Network.RegTest)).Affiliate!;
        var (ok, aff) = await _svc.Apply("other-store", a.Id, AffiliateAction.Approve);
        Assert.False(ok); Assert.Null(aff);
        Assert.Empty(await _svc.List("other-store", null, null));
        Assert.Single(await _svc.List("s7", AffiliateStatus.Pending, "SCOPE"));
    }
}
