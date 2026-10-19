using BTCPayServer.Plugins.MaxiAffiliates.Data;
using BTCPayServer.Plugins.MaxiAffiliates.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NBitcoin;
using Npgsql;

namespace BTCPayServer.Plugins.MaxiAffiliates.Tests;

public class LedgerUnitTests
{
    static Commission C() => new() { StoreId = "s", SaleAmount = 10m, SaleCurrency = "USD", BaseAmount = 200m, BaseCurrency = "MXN", AmountSats = 4_000, Rate = 1_000_000m };
    [Fact] public void RefundFractionBySaleBaseOrBtc()
    {
        Assert.Equal(0.5m, CommissionLedger.RefundFraction(C(), 5m, "usd"));
        Assert.Equal(0.25m, CommissionLedger.RefundFraction(C(), 50m, "MXN"));
        Assert.Equal(0.5m, CommissionLedger.RefundFraction(C(), 0.0001m, "BTC")); // sale = 200 MXN / 1e6 = 0.0002 BTC
        Assert.Equal(0.5m, CommissionLedger.RefundFraction(C(), 10_000m, "SATS"));
        Assert.Equal(1m, CommissionLedger.RefundFraction(C(), 99m, "USD")); // capped
        Assert.Null(CommissionLedger.RefundFraction(C(), 5m, "EUR")); // unknown -> full
        Assert.Null(CommissionLedger.RefundFraction(C(), 0m, "USD"));
    }
    [Theory]
    [InlineData(5_000, 5_000, AffiliateStatus.Active, BalanceState.Due)]
    [InlineData(4_999, 5_000, AffiliateStatus.Active, BalanceState.BelowThreshold)]
    [InlineData(1, 0, AffiliateStatus.Active, BalanceState.Due)]
    [InlineData(-10, 0, AffiliateStatus.Active, BalanceState.NegativeCarryOver)]
    [InlineData(0, 0, AffiliateStatus.Active, BalanceState.Empty)]
    [InlineData(9_000, 5_000, AffiliateStatus.Suspended, BalanceState.AffiliateInactive)]
    public void Threshold(long net, long min, AffiliateStatus st, BalanceState expected) => Assert.Equal(expected, CommissionLedger.Classify(net, min, st));
    [Fact] public void RetryBackoff()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), CommissionEngine.RetryDelay(1));
        Assert.Equal(TimeSpan.FromMinutes(20), CommissionEngine.RetryDelay(3));
        Assert.Equal(TimeSpan.FromHours(6), CommissionEngine.RetryDelay(30));
    }
    [Fact] public void FriendlyScopeLabels()
    {
        Assert.Equal("First payment only", MaxiAffDisplay.ScopeKey(CommissionScope.FirstPayment));
        Assert.Equal("Every payment (lifetime)", MaxiAffDisplay.ScopeKey(CommissionScope.Lifetime));
        var en = System.Text.Json.JsonDocument.Parse(typeof(Plugin).Assembly.GetManifestResourceStream(
            typeof(Plugin).Assembly.GetManifestResourceNames().Single(n => n.EndsWith("en.json")))!).RootElement;
        foreach (var k in Enum.GetValues<CommissionScope>().Select(MaxiAffDisplay.ScopeKey)
                     .Concat(Enum.GetValues<BalanceState>().Select(MaxiAffDisplay.StateKey)).Append("First {0} payments").Append("Percentage").Append("Flat amount"))
            Assert.True(en.TryGetProperty(k, out _), k);
    }
}

[Trait("Category", "Integration")]
public class LedgerIntegrationTests : IAsyncLifetime
{
    const string Server = "Host=127.0.0.1;Port=39372;User ID=postgres";
    readonly string _db = "maxiaff_m5_" + Guid.NewGuid().ToString("N")[..8];
    readonly FixedClock _clock = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
    AffiliateService _aff = null!; CommissionEngine _engine = null!; CommissionLedger _ledger = null!; ProgramSettingsService _prog = null!; Factory _f = null!; bool _ok;
    class Factory(DbContextOptions<MaxiAffiliatesDbContext> o) : IDbContextFactory<MaxiAffiliatesDbContext> { public MaxiAffiliatesDbContext CreateDbContext() => new(o); }
    decimal? _usd = 50_000m;
    Task<decimal?> Rates(string cur) => Task.FromResult(cur == "USD" ? _usd : null);

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
    int _n;
    async Task<Commission> Credit(string buyer, string? code)
    {
        var r = await _engine.CreditSettledInvoice(new("s1", "inv" + (++_n), 10m, "USD", buyer, code, "p", null, "USD"), Rates);
        Assert.Equal(CreditOutcome.Credited, r.Outcome); return r.Commission!;
    }
    async Task<AffiliateBalance> Bal(string affId) => (await _ledger.Balances("s1")).Single(b => b.AffiliateId == affId);
    async Task MarkPaid(string id)
    {
        await using var db = _f.CreateDbContext();
        await db.Commissions.Where(c => c.Id == id).ExecuteUpdateAsync(u => u.SetProperty(c => c.Status, CommissionStatus.Paid).SetProperty(c => c.PaidAt, _clock.Now));
    }

    [Fact]
    public async Task HoldPeriodMaturesOnlyAfterExpiry()
    {
        if (!_ok) Assert.Skip("dev Postgres not running");
        var a = await Active("a@x.io");
        var c = await Credit("b1@x.io", a.Code); // 4,000 sats, payable at +30 d
        Assert.Equal(0, await _ledger.MatureDue("s1"));
        var b = await Bal(a.Id);
        Assert.Equal((4_000L, 0L, BalanceState.Empty), (b.PendingSats, b.PayableSats, b.State));
        Assert.Equal(c.PayableAt, b.NextMaturity);
        _clock.Now = _clock.Now.AddDays(29).AddHours(23);
        Assert.Equal(0, await _ledger.MatureDue("s1"));
        _clock.Now = _clock.Now.AddHours(1);
        Assert.Equal(1, await _ledger.MatureDue("s1"));
        b = await Bal(a.Id);
        Assert.Equal((0L, 4_000L, BalanceState.Due), (b.PendingSats, b.PayableSats, b.State));
        Assert.Equal(0, await _ledger.MatureDue("s1")); // idempotent
    }

    [Fact]
    public async Task RefundBeforePayoutReverses()
    {
        if (!_ok) Assert.Skip("dev Postgres not running");
        var a = await Active("a@x.io");
        var pending = await Credit("b1@x.io", a.Code);
        var payable = await Credit("b2@x.io", a.Code);
        _clock.Now = _clock.Now.AddDays(31); await _ledger.MatureDue("s1");
        // pending commission refunded during the hold period, payable one invalidated
        await using (var db = _f.CreateDbContext())
            await db.Commissions.Where(c => c.Id == pending.Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.Status, CommissionStatus.Pending));
        Assert.Equal(ReversalOutcome.Reversed, (await _ledger.ReverseInvoice("s1", pending.InvoiceId, null, "Invoice refunded", "refund:pp1")).Outcome);
        Assert.Equal(ReversalOutcome.Reversed, (await _ledger.ReverseInvoice("s1", payable.InvoiceId, null, "Invoice marked invalid", "invalid:x")).Outcome);
        Assert.Equal(ReversalOutcome.AlreadyApplied, (await _ledger.ReverseInvoice("s1", payable.InvoiceId, null, "Invoice marked invalid", "invalid:x")).Outcome);
        Assert.Equal(ReversalOutcome.NoCommission, (await _ledger.ReverseInvoice("s1", "nope", null, "r", "r:1")).Outcome);
        Assert.DoesNotContain(await _ledger.Balances("s1"), b => b.AffiliateId == a.Id);
        _clock.Now = _clock.Now.AddDays(60);
        Assert.Equal(0, await _ledger.MatureDue("s1")); // reversed never matures
    }

    [Fact]
    public async Task RefundAfterPayoutNetsAgainstNextBalance()
    {
        if (!_ok) Assert.Skip("dev Postgres not running");
        await _prog.SaveSettings(new AffiliateStoreSettings { StoreId = "s1", MinPayoutSats = 5_000 });
        var a = await Active("a@x.io");
        var paid = await Credit("b1@x.io", a.Code);
        _clock.Now = _clock.Now.AddDays(31); await _ledger.MatureDue("s1"); await MarkPaid(paid.Id);
        var r = await _ledger.ReverseInvoice("s1", paid.InvoiceId, null, "Invoice refunded", "refund:pp9");
        Assert.Equal((ReversalOutcome.ClawedBack, 4_000L), (r.Outcome, r.ClawedSats));
        var b = await Bal(a.Id);
        Assert.Equal((-4_000L, BalanceState.NegativeCarryOver), (b.NetSats, b.State));
        Assert.Equal(ReversalOutcome.AlreadyApplied, (await _ledger.ReverseInvoice("s1", paid.InvoiceId, null, "Invoice refunded", "refund:pp9")).Outcome);
        Assert.Equal(ReversalOutcome.NothingLeft, (await _ledger.ReverseInvoice("s1", paid.InvoiceId, null, "Invoice refunded", "refund:pp10")).Outcome);
        // three new sales mature: 12,000 - 4,000 = 8,000 >= 5,000 -> due
        for (var i = 0; i < 3; i++) await Credit($"n{i}@x.io", a.Code);
        _clock.Now = _clock.Now.AddDays(31); await _ledger.MatureDue("s1");
        b = await Bal(a.Id);
        Assert.Equal((12_000L, -4_000L, 8_000L, BalanceState.Due), (b.PayableSats, b.AdjustmentSats, b.NetSats, b.State));
    }

    [Fact]
    public async Task PartialRefundAndThresholdCarryOver()
    {
        if (!_ok) Assert.Skip("dev Postgres not running");
        await _prog.SaveSettings(new AffiliateStoreSettings { StoreId = "s1", MinPayoutSats = 5_000, HoldDays = 0 });
        var a = await Active("a@x.io");
        var c1 = await Credit("b1@x.io", a.Code);
        await _ledger.MatureDue("s1");
        Assert.Equal(BalanceState.BelowThreshold, (await Bal(a.Id)).State); // 4,000 < 5,000 carries over
        var frac = CommissionLedger.RefundFraction(c1, 2.5m, "USD"); // 25 %
        var r = await _ledger.ReverseInvoice("s1", c1.InvoiceId, frac, "Invoice refunded", "refund:half");
        Assert.Equal((ReversalOutcome.ClawedBack, 1_000L), (r.Outcome, r.ClawedSats));
        Assert.Equal(3_000L, (await Bal(a.Id)).NetSats);
        await Credit("b2@x.io", a.Code); await _ledger.MatureDue("s1");
        var b = await Bal(a.Id);
        Assert.Equal((7_000L, BalanceState.Due), (b.NetSats, b.State)); // carried-over 3,000 + 4,000
        // full reversal of a payable commission with an earlier partial clawback: reversed, its clawback no longer counts
        Assert.Equal(ReversalOutcome.Reversed, (await _ledger.ReverseInvoice("s1", c1.InvoiceId, null, "Invoice marked invalid", "invalid:c1")).Outcome);
        Assert.Equal((4_000L, BalanceState.BelowThreshold), ((await Bal(a.Id)).NetSats, (await Bal(a.Id)).State));
        // suspended affiliate: balance held
        await _aff.Apply("s1", a.Id, AffiliateAction.Suspend);
        Assert.Equal(BalanceState.AffiliateInactive, (await Bal(a.Id)).State);
    }

    [Fact]
    public async Task MissingRateIsTrackedRetriedAndGivenUp()
    {
        if (!_ok) Assert.Skip("dev Postgres not running");
        var a = await Active("a@x.io");
        _usd = null;
        var inv = new SettledInvoice("s1", "invR", 10m, "USD", "r@x.io", a.Code, "p", null, "USD");
        Assert.Equal(CreditOutcome.RateUnavailable, (await _engine.CreditSettledInvoice(inv, Rates)).Outcome);
        var p = (await _ledger.ListPendingCredits("s1")).Single();
        Assert.Equal((1, _clock.Now.AddMinutes(5), false), (p.Attempts, p.NextAttemptAt, p.GaveUp));
        Assert.Empty(await _ledger.DueRetries());
        _clock.Now = _clock.Now.AddMinutes(5);
        Assert.Single(await _ledger.DueRetries());
        await _engine.CreditSettledInvoice(inv, Rates);
        Assert.Equal(2, (await _ledger.ListPendingCredits("s1")).Single().Attempts);
        _clock.Now = _clock.Now.AddDays(8);
        await _engine.CreditSettledInvoice(inv, Rates);
        Assert.True((await _ledger.ListPendingCredits("s1")).Single().GaveUp);
        Assert.Empty(await _ledger.DueRetries());
        Assert.Equal(1, await _ledger.MakeRetriesDue("s1")); // admin "process now"
        Assert.Single(await _ledger.DueRetries());
        _usd = 50_000m;
        var ok = await _engine.CreditSettledInvoice(inv, Rates);
        Assert.Equal(CreditOutcome.Credited, ok.Outcome);
        Assert.Equal(10m, ok.Commission!.SaleAmount);
        Assert.Empty(await _ledger.ListPendingCredits("s1"));
    }
}

[Trait("Category", "Integration")]
public class RetryReplayTests : IAsyncLifetime
{
    const string Server = "Host=127.0.0.1;Port=39372;User ID=postgres";
    readonly string _db = "maxiaff_m5r_" + Guid.NewGuid().ToString("N")[..8];
    bool _ok; IDbContextFactory<MaxiAffiliatesDbContext> _f = null!;
    class Factory(DbContextOptions<MaxiAffiliatesDbContext> o) : IDbContextFactory<MaxiAffiliatesDbContext> { public MaxiAffiliatesDbContext CreateDbContext() => new(o); }
    public async ValueTask InitializeAsync()
    {
        try { await using var a = new NpgsqlConnection(Server + ";Database=postgres"); await a.OpenAsync(); await new NpgsqlCommand($"CREATE DATABASE \"{_db}\"", a).ExecuteNonQueryAsync(); _ok = true; }
        catch (Exception) { return; }
        var o = new DbContextOptionsBuilder<MaxiAffiliatesDbContext>().UseNpgsql($"{Server};Database={_db}").Options;
        await using (var c = new MaxiAffiliatesDbContext(o)) await c.Database.MigrateAsync();
        _f = new Factory(o);
    }
    public async ValueTask DisposeAsync()
    {
        if (!_ok) return; NpgsqlConnection.ClearAllPools();
        await using var a = new NpgsqlConnection(Server + ";Database=postgres"); await a.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_db}\" WITH (FORCE)", a).ExecuteNonQueryAsync();
    }
    [Fact]
    public async Task SeveralSettleEventsCountOneAttempt()
    {
        if (!_ok) Assert.Skip("dev Postgres not running");
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
        var aff = new AffiliateService(_f, clock);
        var a = (await aff.Signup(new SignupRequest("s1", "a@x.io", null, PayoutMethodKind.Lightning, "x@ln.example"), Network.RegTest)).Affiliate!;
        await aff.Apply("s1", a.Id, AffiliateAction.Approve);
        var engine = new CommissionEngine(_f, clock, NullLogger<CommissionEngine>.Instance);
        var inv = new SettledInvoice("s1", "i", 10m, "USD", "b@x.io", a.Code, null, null, "USD");
        for (var i = 0; i < 3; i++) await engine.CreditSettledInvoice(inv, _ => Task.FromResult<decimal?>(null));
        await using var db = _f.CreateDbContext();
        Assert.Equal(1, (await db.PendingCredits.SingleAsync()).Attempts);
    }
}
