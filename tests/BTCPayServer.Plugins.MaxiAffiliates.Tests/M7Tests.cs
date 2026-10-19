using BTCPayServer.Plugins.MaxiAffiliates.Data;
using BTCPayServer.Plugins.MaxiAffiliates.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NBitcoin;
using Npgsql;

namespace BTCPayServer.Plugins.MaxiAffiliates.Tests;

public class PanelTokenTests
{
    [Fact]
    public void TokensAreRandom256BitAndOnlyHashesAreStored()
    {
        var a = PanelAuthService.NewToken(); var b = PanelAuthService.NewToken();
        Assert.NotEqual(a, b);
        Assert.Equal(43, a.Length); // 32 bytes base64url, no padding
        Assert.Matches("^[A-Za-z0-9_-]+$", a);
        Assert.Equal(64, PanelAuthService.Hash(a).Length);
        Assert.NotEqual(a, PanelAuthService.Hash(a));
        Assert.Equal(PanelAuthService.Hash(a), PanelAuthService.Hash(a));
    }
}

[Trait("Category", "Integration")]
public class AffiliatePanelIntegrationTests : IAsyncLifetime
{
    const string Server = "Host=127.0.0.1;Port=39372;User ID=postgres";
    readonly string _db = "maxiaff_m7_" + Guid.NewGuid().ToString("N")[..8];
    readonly FixedClock _clock = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
    AffiliateService _aff = null!; CommissionEngine _engine = null!; CommissionLedger _ledger = null!; ProgramSettingsService _prog = null!;
    PanelAuthService _auth = null!; AffiliatePanelService _panel = null!; Factory _f = null!; bool _ok;
    class Factory(DbContextOptions<MaxiAffiliatesDbContext> o) : IDbContextFactory<MaxiAffiliatesDbContext> { public MaxiAffiliatesDbContext CreateDbContext() => new(o); }

    public async ValueTask InitializeAsync()
    {
        try { await using var a = new NpgsqlConnection(Server + ";Database=postgres"); await a.OpenAsync(); await new NpgsqlCommand($"CREATE DATABASE \"{_db}\"", a).ExecuteNonQueryAsync(); _ok = true; }
        catch (Exception) { return; }
        var o = new DbContextOptionsBuilder<MaxiAffiliatesDbContext>().UseNpgsql($"{Server};Database={_db}").Options;
        await using (var c = new MaxiAffiliatesDbContext(o)) await c.Database.MigrateAsync();
        _f = new Factory(o);
        _aff = new AffiliateService(_f, _clock);
        _engine = new CommissionEngine(_f, _clock, NullLogger<CommissionEngine>.Instance);
        _ledger = new CommissionLedger(_f, _clock, NullLogger<CommissionLedger>.Instance);
        _prog = new ProgramSettingsService(_f, _clock);
        _auth = new PanelAuthService(_f, _clock);
        _panel = new AffiliatePanelService(_f, _ledger, _clock);
        await _prog.SaveSettings(new AffiliateStoreSettings { StoreId = "s1", HoldDays = 30, MinPayoutSats = 5_000 });
    }
    public async ValueTask DisposeAsync()
    {
        if (!_ok) return; NpgsqlConnection.ClearAllPools();
        await using var a = new NpgsqlConnection(Server + ";Database=postgres"); await a.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_db}\" WITH (FORCE)", a).ExecuteNonQueryAsync();
    }
    async Task<Affiliate> Active(string email)
    {
        var a = (await _aff.Signup(new SignupRequest("s1", email, null, PayoutMethodKind.Lightning, "x@ln.example"), Network.RegTest)).Affiliate!;
        await _aff.Apply("s1", a.Id, AffiliateAction.Approve); return a;
    }

    [Fact]
    public async Task MagicLinkIsSingleUseExpiresAndIsRateLimited()
    {
        if (!_ok) Assert.Skip("dev Postgres not running");
        var a = await Active("Panel@X.io");
        Assert.Null(await _auth.CreateMagicLink("s1", "nobody@x.io"));
        Assert.Null(await _auth.CreateMagicLink("s2", "panel@x.io")); // other store
        var l = (await _auth.CreateMagicLink("s1", " PANEL@x.io "))!.Value;
        Assert.Equal(a.Id, l.affiliate.Id);
        Assert.Null(await _auth.Redeem("s2", l.token)); // wrong store
        var session = await _auth.Redeem("s1", l.token);
        Assert.NotNull(session);
        Assert.Null(await _auth.Redeem("s1", l.token)); // single use
        Assert.Equal(a.Id, (await _auth.Authenticate("s1", session))!.Id);
        Assert.Null(await _auth.Authenticate("s1", l.token)); // a link is not a session
        Assert.Null(await _auth.Authenticate("s2", session));

        var late = (await _auth.CreateMagicLink("s1", "panel@x.io"))!.Value;
        _clock.Now += TimeSpan.FromMinutes(16);
        Assert.Null(await _auth.Redeem("s1", late.token)); // expired

        for (var i = 0; i < 3; i++) Assert.NotNull(await _auth.CreateMagicLink("s1", "panel@x.io"));
        Assert.Null(await _auth.CreateMagicLink("s1", "panel@x.io")); // 3 per 15 minutes
        _clock.Now += TimeSpan.FromMinutes(16);
        Assert.NotNull(await _auth.CreateMagicLink("s1", "panel@x.io"));

        await using var db = _f.CreateDbContext();
        Assert.DoesNotContain(await db.AccessTokens.Select(t => t.TokenHash).ToListAsync(), h => h == l.token || h == session);
    }

    [Fact]
    public async Task SessionsExpireLogoutAndBanRevoke()
    {
        if (!_ok) Assert.Skip("dev Postgres not running");
        var a = await Active("s@x.io");
        var s1 = await _auth.Redeem("s1", (await _auth.CreateMagicLink("s1", "s@x.io"))!.Value.token);
        var s2 = await _auth.Redeem("s1", (await _auth.CreateMagicLink("s1", "s@x.io"))!.Value.token);
        await _auth.Logout("s1", s1);
        Assert.Null(await _auth.Authenticate("s1", s1));
        Assert.NotNull(await _auth.Authenticate("s1", s2));
        _clock.Now += TimeSpan.FromDays(8);
        Assert.Null(await _auth.Authenticate("s1", s2)); // 7-day session
        var s3 = await _auth.Redeem("s1", (await _auth.IssueAdminLink("s1", a.Id))!);
        await _aff.Apply("s1", a.Id, AffiliateAction.Suspend);
        Assert.NotNull(await _auth.Authenticate("s1", s3)); // suspended can still look
        await _aff.Apply("s1", a.Id, AffiliateAction.Ban);
        Assert.Null(await _auth.Authenticate("s1", s3)); // banned cannot
        Assert.Null(await _auth.CreateMagicLink("s1", "s@x.io"));
        Assert.Null(await _auth.IssueAdminLink("s1", a.Id));
    }

    [Fact]
    public async Task AdminLinkIsReusableUntilReissuedAndRevokeAllKeepsCurrentSession()
    {
        if (!_ok) Assert.Skip("dev Postgres not running");
        var a = await Active("adm@x.io");
        var first = (await _auth.IssueAdminLink("s1", a.Id))!;
        var sa = await _auth.Redeem("s1", first); var sb = await _auth.Redeem("s1", first);
        Assert.NotNull(sa); Assert.NotNull(sb);
        var second = (await _auth.IssueAdminLink("s1", a.Id))!;
        Assert.Null(await _auth.Redeem("s1", first)); // revoked by reissue
        Assert.NotNull(await _auth.Redeem("s1", second));
        Assert.Null(await _auth.IssueAdminLink("s1", "nope"));
        await _auth.RevokeAll(a.Id, exceptSession: sa);
        Assert.NotNull(await _auth.Authenticate("s1", sa));
        Assert.Null(await _auth.Authenticate("s1", sb));
        Assert.Null(await _auth.Redeem("s1", second));
        _clock.Now += TimeSpan.FromDays(31);
        Assert.Null(await _auth.Redeem("s1", (await _auth.IssueAdminLink("s1", a.Id)) + "x"));
    }

    [Fact]
    public async Task OverviewShowsStatsBalanceCommissionsAndPayouts()
    {
        if (!_ok) Assert.Skip("dev Postgres not running");
        var a = await Active("ov@x.io"); var other = await Active("other@x.io");
        await _panel.CountClick(a.Id); await _panel.CountClick(a.Id);
        async Task<Commission> Sale(Affiliate x, string inv, string buyer) =>
            (await _engine.CreditSettledInvoice(new("s1", inv, 10m, "USD", buyer, x.Code, "p", null, "USD"), _ => Task.FromResult<decimal?>(50_000m))).Commission!;
        var c1 = await Sale(a, "i1", "b1@x.io"); await Sale(a, "i2", "b1@x.io"); var c3 = await Sale(a, "i3", "b2@x.io");
        await Sale(other, "i4", "b3@x.io");
        await _ledger.ReverseInvoice("s1", c3.InvoiceId, null, "Invoice refunded", "refund:1");
        await using (var db = _f.CreateDbContext())
        {
            db.PayoutBatches.Add(new PayoutBatch { StoreId = "s1", AffiliateId = a.Id, AmountSats = 1_000, Destination = "x@ln.example", State = PayoutBatchState.Completed, CreatedAt = _clock.Now, CycleStart = _clock.Now, CycleEnd = _clock.Now });
            db.PayoutBatches.Add(new PayoutBatch { StoreId = "s1", AffiliateId = a.Id, AmountSats = 9, Destination = "x", State = PayoutBatchState.Cancelled, CreatedAt = _clock.Now, CycleStart = _clock.Now, CycleEnd = _clock.Now });
            await db.SaveChangesAsync();
        }
        var me = (await _auth.Authenticate("s1", await _auth.Redeem("s1", (await _auth.IssueAdminLink("s1", a.Id))!)))!;
        var o = await _panel.Overview(me);
        Assert.Equal(new PanelStats(2, 2, 2, 8_000, 1_000, 4_000), o.Stats);
        Assert.Equal((8_000L, 0L, BalanceState.Empty), (o.Balance.PendingSats, o.Balance.PayableSats, o.Balance.State));
        Assert.Equal(c1.CreatedAt.AddDays(30), o.Balance.NextMaturity);
        Assert.Equal(3, o.Commissions.Length);
        Assert.All(o.Commissions, c => Assert.Equal(a.Id, c.AffiliateId)); // never another affiliate's data
        Assert.Single(o.Payouts); // cancelled batches hidden
        _clock.Now += TimeSpan.FromDays(31); await _ledger.MatureDue("s1");
        Assert.Equal(BalanceState.Due, (await _panel.Overview(me)).Balance.State);
    }

    [Fact]
    public async Task PayoutDestinationUpdateIsValidated()
    {
        if (!_ok) Assert.Skip("dev Postgres not running");
        var a = await Active("dest@x.io");
        Assert.NotNull(await _panel.UpdatePayoutDestination(a, PayoutMethodKind.OnChain, "not-an-address", Network.RegTest));
        Assert.NotNull(await _panel.UpdatePayoutDestination(a, PayoutMethodKind.OnChain, "bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kemeawh", Network.RegTest)); // mainnet on regtest
        Assert.NotNull(await _panel.UpdatePayoutDestination(a, PayoutMethodKind.Lightning, "nope", Network.RegTest));
        Assert.Null(await _panel.UpdatePayoutDestination(a, PayoutMethodKind.OnChain, "bcrt1qw508d6qejxtdg4y5r3zarvary0c5xw7kygt080", Network.RegTest));
        await using (var db = _f.CreateDbContext())
        {
            var x = await db.Affiliates.SingleAsync(z => z.Id == a.Id);
            Assert.Equal((PayoutMethodKind.OnChain, "bcrt1qw508d6qejxtdg4y5r3zarvary0c5xw7kygt080", (DateTimeOffset?)_clock.Now), (x.PayoutMethod, x.PayoutDestination, x.PayoutDestinationUpdatedAt));
        }
        await _prog.SaveSettings(new AffiliateStoreSettings { StoreId = "s1", HoldDays = 30, MinPayoutSats = 5_000, LightningEnabled = false });
        Assert.Equal("This payout method is not available.", await _panel.UpdatePayoutDestination(a, PayoutMethodKind.Lightning, "y@ln.example", Network.RegTest));
    }
}
