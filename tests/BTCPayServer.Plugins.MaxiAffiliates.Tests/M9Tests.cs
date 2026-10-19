using System.Reflection;
using BTCPayServer.Plugins.MaxiAffiliates.Data;
using BTCPayServer.Plugins.MaxiAffiliates.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NBitcoin;
using NicolasDorier.RateLimits;
using Npgsql;

namespace BTCPayServer.Plugins.MaxiAffiliates.Tests;

/// <summary>M9: every endpoint's authz / CSRF / rate-limit posture is pinned so a new action cannot slip in unprotected.</summary>
public class EndpointPostureTests
{
    static readonly (Type t, MethodInfo m)[] Actions = typeof(Plugin).Assembly.GetTypes()
        .Where(t => typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract)
        .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any()).Select(m => (t, m))).ToArray();

    static readonly HashSet<string> Anonymous =
    [
        "MaxiAffiliatesController.Health", "UIReferralController.Click", "UIReferralController.Script",
        "UIReferralController.CheckoutPost", "UIReferralController.CheckoutGet",
        "UIAffiliateSignupController.Signup", "UIAffiliatePanelController.Login", "UIAffiliatePanelController.Auth",
        "UIAffiliatePanelController.AuthPost", "UIAffiliatePanelController.Index", "UIAffiliatePanelController.UpdatePayout",
        "UIAffiliatePanelController.Logout",
    ];
    static string N((Type t, MethodInfo m) a) => $"{a.t.Name}.{a.m.Name}";
    static bool IsAnon((Type t, MethodInfo m) a) => a.t.GetCustomAttribute<AllowAnonymousAttribute>() != null || a.m.GetCustomAttribute<AllowAnonymousAttribute>() != null;
    static string Route((Type t, MethodInfo m) a) => a.m.GetCustomAttributes<HttpMethodAttribute>().First().Template ?? "";
    static bool IsPost((Type t, MethodInfo m) a) => a.m.GetCustomAttributes<HttpMethodAttribute>().Any(h => h.HttpMethods.Contains("POST"));

    [Fact] public void InventoryIsComplete() => Assert.True(Actions.Length >= 30, $"{Actions.Length} actions");

    [Fact]
    public void OnlyKnownActionsAreAnonymous() =>
        Assert.Empty(Actions.Where(IsAnon).Select(N).Where(n => !Anonymous.Contains(n)));

    [Fact]
    public void AdminActionsAreStoreScopedAndPolicyProtected()
    {
        foreach (var a in Actions.Where(a => !IsAnon(a)))
        {
            Assert.StartsWith("~/stores/{storeId}/plugins/maxiaffiliates/", Route(a));
            var policies = a.t.GetCustomAttributes<AuthorizeAttribute>().Concat(a.m.GetCustomAttributes<AuthorizeAttribute>()).Select(x => x.Policy).ToList();
            Assert.Contains(MaxiAffiliatesPolicies.CanViewAffiliates, policies);
            if (IsPost(a)) Assert.True(policies.Contains(MaxiAffiliatesPolicies.CanManageAffiliates), $"{N(a)} mutates without CanManageAffiliates");
        }
    }

    [Fact]
    public void EveryPostIsCsrfProtectedExceptTheCorsCheckout()
    {
        var missing = Actions.Where(IsPost).Where(a => a.m.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() is null).Select(N).ToArray();
        Assert.Equal(["UIReferralController.CheckoutPost"], missing); // public JSON endpoint (CORS, no cookies needed); only creates a checkout
    }

    [Theory]
    [InlineData(typeof(Controllers.UIAffiliateSignupController), "Signup", ZoneLimits.Register)]
    [InlineData(typeof(Controllers.UIAffiliatePanelController), "Login", ZoneLimits.Login)]
    [InlineData(typeof(Controllers.UIAffiliatePanelController), "AuthPost", ZoneLimits.Login)]
    [InlineData(typeof(Controllers.UIReferralController), "CheckoutPost", ZoneLimits.PublicInvoices)]
    [InlineData(typeof(Controllers.UIReferralController), "CheckoutGet", ZoneLimits.PublicInvoices)]
    public void PublicWritesAreRateLimited(Type t, string method, string zone)
    {
        var m = t.GetMethods().Where(x => x.Name == method && x.GetCustomAttributes<HttpMethodAttribute>().Any(h => h.HttpMethods.Contains(method.StartsWith("CheckoutGet") ? "GET" : "POST"))).Single();
        Assert.Equal(zone, m.GetCustomAttribute<RateLimitsFilterAttribute>()?.ZoneName);
    }
}

public class InputSafetyTests
{
    [Theory]
    [InlineData(" Ana@Example.COM ", "ana@example.com")]
    [InlineData("a.b+tag@sub.example.mx", "a.b+tag@sub.example.mx")]
    [InlineData("=HYPERLINK(\"x\")@evil.com", null)]
    [InlineData("Ana <ana@x.io>", null)]
    [InlineData("ana@localhost", null)]
    [InlineData("a b@x.io", null)]
    [InlineData("<script>@x.io", null)]
    [InlineData("", null)]
    public void Email(string input, string? expected) => Assert.Equal(expected, InputSafety.NormalizeEmail(input));

    [Theory]
    [InlineData("=cmd|' /C calc'!A0", "'=cmd|' /C calc'!A0")]
    [InlineData("+1+1", "'+1+1")] [InlineData("@SUM(A1)", "'@SUM(A1)")] [InlineData("-2+3", "'-2+3")]
    [InlineData("-655", "-655")] [InlineData("ana@x.io", "ana@x.io")] [InlineData("\tx", "'\tx")]
    public void Spreadsheet(string v, string expected) => Assert.Equal(expected, InputSafety.SpreadsheetSafe(v));

    [Theory]
    [InlineData("https://cointeligencia.com/gracias", "https://cointeligencia.com/", "pay.x.com", "https://cointeligencia.com/gracias")]
    [InlineData("https://pay.x.com/receipt", "https://cointeligencia.com/", "pay.x.com", "https://pay.x.com/receipt")]
    [InlineData("https://evil.example/phish", "https://cointeligencia.com/", "pay.x.com", null)]
    [InlineData("https://cointeligencia.com.evil.example/", "https://cointeligencia.com/", "pay.x.com", null)]
    [InlineData("https://cointeligencia.com@evil.example/", "https://cointeligencia.com/", "pay.x.com", null)]
    [InlineData("https://user@cointeligencia.com/", "https://cointeligencia.com/", "pay.x.com", null)]
    [InlineData("javascript:alert(1)", "https://cointeligencia.com/", "pay.x.com", null)]
    [InlineData("//evil.example/", "https://cointeligencia.com/", "pay.x.com", null)]
    [InlineData("https://cointeligencia.com/", null, "pay.x.com", null)]
    public void SuccessRedirect(string url, string? landing, string host, string? expected) =>
        Assert.Equal(expected, InputSafety.SafeSuccessRedirect(url, landing, host));
}

public class ReleaseHygieneTests
{
    static string Src()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "src", "BTCPayServer.Plugins.MaxiAffiliates"))) d = d.Parent;
        return Path.Combine(d!.FullName, "src", "BTCPayServer.Plugins.MaxiAffiliates");
    }
    static IEnumerable<string> Files() => Directory.EnumerateFiles(Src(), "*.*", SearchOption.AllDirectories)
        .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/") && (f.EndsWith(".cs") || f.EndsWith(".cshtml") || f.EndsWith(".json") || f.EndsWith(".js") || f.EndsWith(".csproj")));

    [Fact]
    public void NoSsrfExceptionOrDevHostsInThePlugin()
    {
        var bad = Files().Where(f => { var t = File.ReadAllText(f); return t.Contains("SSRF", StringComparison.OrdinalIgnoreCase) || t.Contains("127.0.0.1") || t.Contains("localhost:"); }).ToArray();
        Assert.Empty(bad);
    }

    [Fact]
    public void PluginMakesNoOutboundHttpCalls()
    {
        var bad = Files().Where(f => f.EndsWith(".cs")).Where(f => { var t = File.ReadAllText(f); return t.Contains("HttpClient") || t.Contains("WebRequest"); }).ToArray();
        Assert.Empty(bad);
    }
}

[Trait("Category", "Integration")]
public class SecurityIntegrationTests : IAsyncLifetime
{
    const string Server = "Host=127.0.0.1;Port=39372;User ID=postgres";
    readonly string _db = "maxiaff_m9_" + Guid.NewGuid().ToString("N")[..8];
    readonly FixedClock _clock = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
    readonly FakeGateway _gw = new();
    AffiliateService _aff = null!; CommissionEngine _engine = null!; CommissionLedger _ledger = null!; ProgramSettingsService _prog = null!;
    PayoutCycleService _cycles = null!; PanelAuthService _auth = null!; Factory _f = null!; bool _ok;
    class Factory(DbContextOptions<MaxiAffiliatesDbContext> o) : IDbContextFactory<MaxiAffiliatesDbContext> { public MaxiAffiliatesDbContext CreateDbContext() => new(o); }

    public async ValueTask InitializeAsync()
    {
        try { await using var a = new NpgsqlConnection(Server + ";Database=postgres"); await a.OpenAsync(); await new NpgsqlCommand($"CREATE DATABASE \"{_db}\"", a).ExecuteNonQueryAsync(); _ok = true; }
        catch (Exception) { return; }
        var o = new DbContextOptionsBuilder<MaxiAffiliatesDbContext>().UseNpgsql($"{Server};Database={_db};Maximum Pool Size=30").Options;
        await using (var c = new MaxiAffiliatesDbContext(o)) await c.Database.MigrateAsync();
        _f = new Factory(o);
        _aff = new AffiliateService(_f, _clock);
        _engine = new CommissionEngine(_f, _clock, NullLogger<CommissionEngine>.Instance);
        _ledger = new CommissionLedger(_f, _clock, NullLogger<CommissionLedger>.Instance);
        _prog = new ProgramSettingsService(_f, _clock);
        _cycles = new PayoutCycleService(_f, _ledger, _gw, _clock, NullLogger<PayoutCycleService>.Instance);
        _auth = new PanelAuthService(_f, _clock);
        var at = _clock.Now; _clock.Now = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        await _prog.SaveSettings(new AffiliateStoreSettings { StoreId = "s1", HoldDays = 0, MinPayoutSats = 1_000 });
        await _prog.SaveSettings(new AffiliateStoreSettings { StoreId = "s2", HoldDays = 0, MinPayoutSats = 1_000 });
        _clock.Now = at;
    }
    public async ValueTask DisposeAsync()
    {
        if (!_ok) return; NpgsqlConnection.ClearAllPools();
        await using var a = new NpgsqlConnection(Server + ";Database=postgres"); await a.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_db}\" WITH (FORCE)", a).ExecuteNonQueryAsync();
    }
    int _n;
    async Task<Affiliate> WithSales(string store, string email, int sales)
    {
        var a = (await _aff.Signup(new SignupRequest(store, email, null, PayoutMethodKind.Lightning, "x@ln.example"), Network.RegTest)).Affiliate!;
        await _aff.Apply(store, a.Id, AffiliateAction.Approve);
        for (var i = 0; i < sales; i++)
            Assert.Equal(CreditOutcome.Credited, (await _engine.CreditSettledInvoice(new(store, "inv" + (++_n), 10m, "USD", $"b{_n}@x.io", a.Code, "p", null, "USD"), _ => Task.FromResult<decimal?>(50_000m))).Outcome);
        await _ledger.MatureDue(store);
        return a;
    }

    [Fact]
    public async Task ConcurrentCycleRunsNeverBatchACommissionTwice()
    {
        if (!_ok) Assert.Skip("dev Postgres not running");
        var a = await WithSales("s1", "a@x.io", 3); var b = await WithSales("s1", "b@x.io", 2);
        var runs = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => _cycles.RunCycle("s1", manual: true))));
        await using var db = _f.CreateDbContext();
        var batched = await db.PayoutBatches.Where(x => x.StoreId == "s1").SumAsync(x => x.AmountSats);
        var total = await db.Commissions.Where(c => c.StoreId == "s1").SumAsync(c => c.AmountSats);
        Assert.Equal(total, batched);
        Assert.Equal(2, await db.PayoutBatches.CountAsync(x => x.StoreId == "s1"));
        Assert.Equal(0, await db.Commissions.CountAsync(c => c.StoreId == "s1" && c.PayoutBatchId == null));
    }

    [Fact]
    public async Task ConcurrentApprovalsCreateExactlyOnePayout()
    {
        if (!_ok) Assert.Skip("dev Postgres not running");
        await WithSales("s1", "a@x.io", 2);
        var cycle = (await _cycles.RunCycle("s1", manual: true)).Cycle!;
        await using var db = _f.CreateDbContext();
        var batch = await db.PayoutBatches.SingleAsync(x => x.CycleId == cycle.Id);
        var r = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => _cycles.Approve("s1", batch.Id))));
        Assert.Single(r, x => x.Outcome == ApproveOutcome.Approved);
        Assert.Single(_gw.Calls);
        Assert.False(await _cycles.Cancel("s1", batch.Id)); // approved batches cannot be cancelled back into the balance
    }

    [Fact]
    public async Task OtherStoreCannotTouchThisStoresObjects()
    {
        if (!_ok) Assert.Skip("dev Postgres not running");
        var a = await WithSales("s1", "a@x.io", 2);
        var cycle = (await _cycles.RunCycle("s1", manual: true)).Cycle!;
        await using var db = _f.CreateDbContext();
        var batch = await db.PayoutBatches.SingleAsync(x => x.CycleId == cycle.Id);
        var com = await db.Commissions.FirstAsync(c => c.StoreId == "s1");
        var rule = await _prog.UpsertRule(new CommissionRule { StoreId = "s1", PlanId = "p", Type = CommissionType.Percent, Value = 10, Scope = CommissionScope.Lifetime, Enabled = true });

        Assert.Equal(ApproveOutcome.NotFound, (await _cycles.Approve("s2", batch.Id)).Outcome);
        Assert.False(await _cycles.Cancel("s2", batch.Id));
        Assert.Null((await _cycles.GetCycle("s2", cycle.Id)).Item1);
        Assert.Equal(ReversalOutcome.NoCommission, (await _ledger.ReverseCommission("s2", com.Id, null, "x", "k")).Outcome);
        Assert.False(await _prog.DeleteRule("s2", rule.Id));
        Assert.Null(await _auth.IssueAdminLink("s2", a.Id));
        Assert.False((await _aff.Apply("s2", a.Id, AffiliateAction.Ban)).ok);
        Assert.Empty(_gw.Calls);
        Assert.Equal(AffiliateStatus.Active, (await db.Affiliates.AsNoTracking().SingleAsync(x => x.Id == a.Id)).Status);
    }

    [Fact]
    public async Task SignupRejectsFormulaAndDisplayNameEmails()
    {
        if (!_ok) Assert.Skip("dev Postgres not running");
        foreach (var e in new[] { "=HYPERLINK(\"http://x\")@x.io", "Ana <ana@x.io>", "ana@localhost" })
            Assert.False((await _aff.Signup(new SignupRequest("s1", e, null, PayoutMethodKind.Lightning, "x@ln.example"), Network.RegTest)).Success);
    }
}
