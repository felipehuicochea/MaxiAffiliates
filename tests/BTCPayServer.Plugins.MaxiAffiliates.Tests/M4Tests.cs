using BTCPayServer.Plugins.MaxiAffiliates.Data;
using BTCPayServer.Plugins.MaxiAffiliates.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NBitcoin;
using Npgsql;

namespace BTCPayServer.Plugins.MaxiAffiliates.Tests;

public class CommissionCalculatorTests
{
    [Fact] public void PercentSameCurrency()
    {
        var q = CommissionCalculator.Compute(10m, "USD", "USD", 50_000m, 50_000m, CommissionType.Percent, 20m);
        Assert.Equal(10m, q.BaseAmount); Assert.Equal(2m, q.CommissionAmount); Assert.Equal(4_000L, q.AmountSats);
    }
    [Fact] public void ConvertsToBaseCurrency()
    {
        // 10 USD at 50k USD/BTC = 0.0002 BTC; at 1,000,000 MXN/BTC = 200 MXN
        var q = CommissionCalculator.Compute(10m, "USD", "MXN", 50_000m, 1_000_000m, CommissionType.Percent, 20m);
        Assert.Equal(200m, q.BaseAmount); Assert.Equal(40m, q.CommissionAmount); Assert.Equal(4_000L, q.AmountSats);
    }
    [Fact] public void FlatIsCappedAtSale()
    {
        Assert.Equal(3m, CommissionCalculator.Compute(10m, "USD", "USD", 50_000m, 50_000m, CommissionType.Flat, 3m).CommissionAmount);
        Assert.Equal(10m, CommissionCalculator.Compute(10m, "USD", "USD", 50_000m, 50_000m, CommissionType.Flat, 99m).CommissionAmount);
    }
    [Fact] public void PercentClamped() =>
        Assert.Equal(10m, CommissionCalculator.Compute(10m, "USD", "USD", 1m, 1m, CommissionType.Percent, 250m).CommissionAmount);
    [Fact] public void BtcAndSats()
    {
        Assert.Equal(2_000_000L, CommissionCalculator.Compute(0.1m, "BTC", "BTC", 0, 0, CommissionType.Percent, 20m).AmountSats);
        var q = CommissionCalculator.Compute(10_000m, "SATS", "USD", 0, 50_000m, CommissionType.Percent, 10m);
        Assert.Equal(5m, q.BaseAmount); Assert.Equal(1_000L, q.AmountSats);
    }
    [Fact] public void SatsRoundTowardZero() =>
        Assert.Equal(1L, CommissionCalculator.Compute(1m, "USD", "USD", 66_666_666m, 66_666_666m, CommissionType.Percent, 100m).AmountSats);
    [Theory]
    [InlineData(CommissionScope.FirstPayment, null, 0, true)] [InlineData(CommissionScope.FirstPayment, null, 1, false)]
    [InlineData(CommissionScope.Lifetime, null, 99, true)]
    [InlineData(CommissionScope.FirstN, 3, 2, true)] [InlineData(CommissionScope.FirstN, 3, 3, false)] [InlineData(CommissionScope.FirstN, null, 1, false)]
    public void Scope(CommissionScope s, int? n, int done, bool expected) => Assert.Equal(expected, CommissionCalculator.InScope(s, n, done));
}

public class RulePickTests
{
    static readonly AffiliateStoreSettings S = new() { StoreId = "s", DefaultPercent = 20, DefaultScope = CommissionScope.Lifetime };
    [Fact] public void FallsBackToSettings() => Assert.Equal(20m, CommissionEngine.PickRule(S, [], "p1").Value);
    [Fact] public void StoreRuleThenPlanRule()
    {
        var store = new CommissionRule { StoreId = "s", Value = 10 };
        var plan = new CommissionRule { StoreId = "s", PlanId = "p1", Value = 30, Scope = CommissionScope.FirstN, FirstN = 2 };
        var off = new CommissionRule { StoreId = "s", PlanId = "p2", Value = 50, Enabled = false };
        Assert.Equal(30m, CommissionEngine.PickRule(S, [store, plan, off], "p1").Value);
        Assert.Equal(10m, CommissionEngine.PickRule(S, [store, plan, off], "p2").Value); // disabled plan rule ignored
        Assert.Equal(10m, CommissionEngine.PickRule(S, [store, plan], null).Value);
    }
    [Fact] public void BaseCurrency()
    {
        Assert.Equal("USD", CommissionEngine.BaseCurrency(new() { StoreId = "s" }, "usd"));
        Assert.Equal("MXN", CommissionEngine.BaseCurrency(new() { StoreId = "s", BaseCurrencyOverride = " mxn " }, "USD"));
    }
    [Fact] public void SettingsValidation()
    {
        Assert.Null(ProgramSettingsService.Validate(new() { StoreId = "s" }));
        Assert.NotNull(ProgramSettingsService.Validate(new() { StoreId = "s", DefaultPercent = 101 }));
        Assert.NotNull(ProgramSettingsService.Validate(new() { StoreId = "s", DefaultScope = CommissionScope.FirstN }));
        Assert.NotNull(ProgramSettingsService.Validate(new() { StoreId = "s", BaseCurrencyOverride = "US$" }));
        Assert.NotNull(ProgramSettingsService.Validate(new() { StoreId = "s", LightningEnabled = false, OnChainEnabled = false }));
        Assert.NotNull(ProgramSettingsService.ValidateRule(new() { StoreId = "s", Type = CommissionType.Percent, Value = 150 }));
    }
}

[Trait("Category", "Integration")]
public class CommissionEngineIntegrationTests : IAsyncLifetime
{
    const string Server = "Host=127.0.0.1;Port=39372;User ID=postgres";
    readonly string _db = "maxiaff_m4_" + Guid.NewGuid().ToString("N")[..8];
    readonly FixedClock _clock = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
    AffiliateService _aff = null!; CommissionEngine _engine = null!; ProgramSettingsService _prog = null!; Factory _f = null!; bool _ok;
    class Factory(DbContextOptions<MaxiAffiliatesDbContext> o) : IDbContextFactory<MaxiAffiliatesDbContext> { public MaxiAffiliatesDbContext CreateDbContext() => new(o); }
    static Task<decimal?> Rates(string cur) => Task.FromResult<decimal?>(cur switch { "USD" => 50_000m, "MXN" => 1_000_000m, _ => null });

    public async ValueTask InitializeAsync()
    {
        try { await using var a = new NpgsqlConnection(Server + ";Database=postgres"); await a.OpenAsync(); await new NpgsqlCommand($"CREATE DATABASE \"{_db}\"", a).ExecuteNonQueryAsync(); _ok = true; }
        catch (Exception) { return; }
        var o = new DbContextOptionsBuilder<MaxiAffiliatesDbContext>().UseNpgsql($"{Server};Database={_db}").Options;
        await using (var c = new MaxiAffiliatesDbContext(o)) await c.Database.MigrateAsync();
        _f = new Factory(o);
        _aff = new AffiliateService(_f, _clock);
        _engine = new CommissionEngine(_f, _clock, NullLogger<CommissionEngine>.Instance);
        _prog = new ProgramSettingsService(_f, _clock);
    }
    public async ValueTask DisposeAsync()
    {
        if (!_ok) return; NpgsqlConnection.ClearAllPools();
        await using var a = new NpgsqlConnection(Server + ";Database=postgres"); await a.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_db}\" WITH (FORCE)", a).ExecuteNonQueryAsync();
    }
    async Task<Affiliate> Active(string store, string email)
    {
        var a = (await _aff.Signup(new SignupRequest(store, email, null, PayoutMethodKind.Lightning, "x@ln.example"), Network.RegTest)).Affiliate!;
        await _aff.Apply(store, a.Id, AffiliateAction.Approve); return a;
    }
    int _n;
    SettledInvoice Inv(string store, string email, string? @ref, string? plan = "plan1", decimal price = 10m, string cur = "USD") =>
        new(store, "inv" + (++_n), price, cur, email, @ref, plan, "sub-" + email, "USD");
    Task<CreditResult> Credit(SettledInvoice i) => _engine.CreditSettledInvoice(i, Rates);

    [Fact]
    public async Task LifetimeDefaultCreditsInitialAndRenewals()
    {
        if (!_ok) Assert.Skip("dev Postgres not running");
        var alice = await Active("s1", "alice@example.com");
        var r1 = await Credit(Inv("s1", "buyer@example.com", alice.Code));
        Assert.Equal(CreditOutcome.Credited, r1.Outcome);
        Assert.Equal(2m, r1.Commission!.CommissionAmount); Assert.Equal(4_000L, r1.Commission.AmountSats);
        Assert.Equal(CommissionStatus.Pending, r1.Commission.Status);
        Assert.Equal(_clock.Now.AddDays(30), r1.Commission.PayableAt);
        // renewal: no ref on invoice, same buyer email -> same binding
        var r2 = await Credit(Inv("s1", "BUYER@example.com", null));
        Assert.Equal(CreditOutcome.Credited, r2.Outcome); Assert.Equal(alice.Id, r2.Commission!.AffiliateId);
        await using var db = _f.CreateDbContext();
        Assert.Equal(2, (await db.Referrals.SingleAsync()).CreditedPayments);
    }

    [Fact]
    public async Task FirstPaymentAndFirstNScopes()
    {
        if (!_ok) Assert.Skip("dev Postgres not running");
        var alice = await Active("s1", "alice@example.com");
        await _prog.UpsertRule(new CommissionRule { StoreId = "s1", PlanId = "once", Value = 50, Scope = CommissionScope.FirstPayment });
        await _prog.UpsertRule(new CommissionRule { StoreId = "s1", PlanId = "three", Value = 10, Scope = CommissionScope.FirstN, FirstN = 3 });
        Assert.Equal(CreditOutcome.Credited, (await Credit(Inv("s1", "a@b.c", alice.Code, "once"))).Outcome);
        Assert.Equal(CreditOutcome.OutOfScope, (await Credit(Inv("s1", "a@b.c", null, "once"))).Outcome);
        var outcomes = new List<CreditOutcome>();
        for (var i = 0; i < 5; i++) outcomes.Add((await Credit(Inv("s1", "x@y.z", i == 0 ? alice.Code : null, "three"))).Outcome);
        Assert.Equal([CreditOutcome.Credited, CreditOutcome.Credited, CreditOutcome.Credited, CreditOutcome.OutOfScope, CreditOutcome.OutOfScope], outcomes);
    }

    [Fact]
    public async Task FlatRuleAndBaseCurrencyOverride()
    {
        if (!_ok) Assert.Skip("dev Postgres not running");
        var alice = await Active("s1", "alice@example.com");
        await _prog.SaveSettings(new AffiliateStoreSettings { StoreId = "s1", BaseCurrencyOverride = "mxn", HoldDays = 0 });
        var pct = await Credit(Inv("s1", "p@q.r", alice.Code));
        Assert.Equal("MXN", pct.Commission!.BaseCurrency); Assert.Equal(200m, pct.Commission.BaseAmount); Assert.Equal(40m, pct.Commission.CommissionAmount);
        Assert.Equal(4_000L, pct.Commission.AmountSats); Assert.Equal(_clock.Now, pct.Commission.PayableAt);
        await _prog.UpsertRule(new CommissionRule { StoreId = "s1", Type = CommissionType.Flat, Value = 25 }); // store default rule
        var flat = await Credit(Inv("s1", "f@q.r", alice.Code, "other"));
        Assert.Equal(25m, flat.Commission!.CommissionAmount); Assert.Equal(2_500L, flat.Commission.AmountSats);
    }

    [Fact]
    public async Task GuardRails()
    {
        if (!_ok) Assert.Skip("dev Postgres not running");
        var alice = await Active("s1", "alice@example.com");
        var bob = await Active("s1", "bob@example.com");
        var inv = Inv("s1", "c@d.e", alice.Code);
        Assert.Equal(CreditOutcome.Credited, (await Credit(inv)).Outcome);
        Assert.Equal(CreditOutcome.AlreadyCredited, (await Credit(inv)).Outcome); // replayed event
        // existing binding wins over a new ref (first click)
        var r = await Credit(Inv("s1", "c@d.e", bob.Code));
        Assert.Equal(alice.Id, r.Commission!.AffiliateId);
        Assert.Equal(CreditOutcome.SelfReferral, (await Credit(Inv("s1", "alice@example.com", alice.Code))).Outcome);
        Assert.Equal(CreditOutcome.NoReferral, (await Credit(Inv("s1", "n@o.p", null))).Outcome);
        Assert.Equal(CreditOutcome.AffiliateInactive, (await Credit(Inv("s1", "n@o.p", "NOSUCH12"))).Outcome);
        Assert.Equal(CreditOutcome.RateUnavailable, (await Credit(Inv("s1", "r@a.t", alice.Code, cur: "XYZ"))).Outcome);
        // suspended affiliate keeps the binding but earns nothing
        await _aff.Apply("s1", alice.Id, AffiliateAction.Suspend);
        Assert.Equal(CreditOutcome.AffiliateInactive, (await Credit(Inv("s1", "c@d.e", null))).Outcome);
        // other store is isolated
        Assert.Equal(CreditOutcome.AffiliateInactive, (await Credit(Inv("s2", "c@d.e", bob.Code))).Outcome);
        var rows = await _prog.ListCommissions("s1", null);
        Assert.Equal(2, rows.Length); Assert.All(rows, x => Assert.Equal("email:c@d.e", x.CustomerKey));
    }

    [Fact]
    public async Task DisabledProgramCreditsNothing()
    {
        if (!_ok) Assert.Skip("dev Postgres not running");
        var alice = await Active("s1", "alice@example.com");
        await _prog.SaveSettings(new AffiliateStoreSettings { StoreId = "s1", Enabled = false });
        Assert.Equal(CreditOutcome.NoReferral, (await Credit(Inv("s1", "z@z.z", alice.Code))).Outcome);
    }
}
