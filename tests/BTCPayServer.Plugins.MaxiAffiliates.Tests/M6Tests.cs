using BTCPayServer.Client.Models;
using BTCPayServer.Plugins.MaxiAffiliates.Data;
using BTCPayServer.Plugins.MaxiAffiliates.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NBitcoin;
using Newtonsoft.Json.Linq;
using Npgsql;

namespace BTCPayServer.Plugins.MaxiAffiliates.Tests;

public class CycleScheduleTests
{
    static DateTimeOffset D(int y, int m, int d, int h = 12) => new(y, m, d, h, 0, 0, TimeSpan.Zero);
    [Fact] public void Monthly()
    {
        Assert.Equal(("M-2026-10-01", D(2026, 10, 1, 0)), PayoutCycleService.CurrentCycle(PayoutFrequency.Monthly, 1, D(2026, 10, 19)));
        Assert.Equal(("M-2026-09-15", D(2026, 9, 15, 0)), PayoutCycleService.CurrentCycle(PayoutFrequency.Monthly, 15, D(2026, 10, 14)));
        Assert.Equal("M-2026-10-15", PayoutCycleService.CurrentCycle(PayoutFrequency.Monthly, 15, D(2026, 10, 15, 0)).key);
        Assert.Equal("M-2025-12-28", PayoutCycleService.CurrentCycle(PayoutFrequency.Monthly, 31, D(2026, 1, 5)).key); // clamped to 28
    }
    [Fact] public void Weekly()
    {
        // 2026-10-19 is a Monday
        Assert.Equal("W-2026-10-19", PayoutCycleService.CurrentCycle(PayoutFrequency.Weekly, 1, D(2026, 10, 19)).key);
        Assert.Equal("W-2026-10-18", PayoutCycleService.CurrentCycle(PayoutFrequency.Weekly, 0, D(2026, 10, 19)).key);
        Assert.Equal("W-2026-10-13", PayoutCycleService.CurrentCycle(PayoutFrequency.Weekly, 2, D(2026, 10, 19)).key);
    }
    [Fact] public void CsvEscapesAndBlocksFormulas()
    {
        var c = new PayoutCycle { StoreId = "s", CycleKey = "M-2026-10-01", PeriodEnd = D(2026, 10, 1, 0) };
        var row = new PayoutCycleService.BatchRow(new PayoutBatch { PayoutMethod = PayoutMethodKind.OnChain, Destination = "bcrt1qxyz", AmountSats = 12_345, GrossSats = 13_000, AdjustmentSats = -655, CommissionCount = 3, State = PayoutBatchState.Completed, PayoutId = "po1" },
            "a@x.io", "ABC", "=HYPERLINK(\"x\"), Inc");
        var lines = PayoutCycleService.ToCsv(c, [row]).Split("\r\n");
        Assert.StartsWith("cycle,period_end,affiliate_email", lines[0]);
        Assert.Equal("M-2026-10-01,2026-10-01,a@x.io,\"'=HYPERLINK(\"\"x\"\"), Inc\",ABC,OnChain,bcrt1qxyz,3,13000,-655,12345,0.00012345,Completed,po1,", lines[1]);
    }
}

class FakeGateway : IPayoutGateway
{
    public List<(PayoutMethodKind m, string dest, long sats)> Calls = new();
    public string? FailWith;
    public Task<PayoutCreateResult> CreatePayout(string storeId, PayoutMethodKind method, string destination, long sats, JObject metadata, CancellationToken ct)
    {
        Calls.Add((method, destination, sats));
        return Task.FromResult(FailWith is null ? new PayoutCreateResult("po" + Calls.Count, null) : new PayoutCreateResult(null, FailWith));
    }
}

[Trait("Category", "Integration")]
public class PayoutCycleIntegrationTests : IAsyncLifetime
{
    const string Server = "Host=127.0.0.1;Port=39372;User ID=postgres";
    readonly string _db = "maxiaff_m6_" + Guid.NewGuid().ToString("N")[..8];
    readonly FixedClock _clock = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
    readonly FakeGateway _gw = new();
    AffiliateService _aff = null!; CommissionEngine _engine = null!; CommissionLedger _ledger = null!; ProgramSettingsService _prog = null!; PayoutCycleService _cycles = null!; Factory _f = null!; bool _ok;
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
        _cycles = new PayoutCycleService(_f, _ledger, _gw, _clock, NullLogger<PayoutCycleService>.Instance);
    }
    public async ValueTask DisposeAsync()
    {
        if (!_ok) return; NpgsqlConnection.ClearAllPools();
        await using var a = new NpgsqlConnection(Server + ";Database=postgres"); await a.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_db}\" WITH (FORCE)", a).ExecuteNonQueryAsync();
    }
    async Task Settings(bool auto = false, long min = 5_000, PayoutFrequency f = PayoutFrequency.Monthly, int day = 1)
    {
        var at = _clock.Now; _clock.Now = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero); // configured long ago
        await _prog.SaveSettings(new AffiliateStoreSettings { StoreId = "s1", HoldDays = 0, MinPayoutSats = min, AutoApprovePayouts = auto, PayoutFrequency = f, PayoutDay = day });
        _clock.Now = at;
    }
    async Task<Affiliate> Active(string email, PayoutMethodKind m = PayoutMethodKind.Lightning)
    {
        var dest = m == PayoutMethodKind.Lightning ? "x@ln.example" : "bcrt1qw508d6qejxtdg4y5r3zarvary0c5xw7kygt080";
        var a = (await _aff.Signup(new SignupRequest("s1", email, null, m, dest), Network.RegTest)).Affiliate!;
        await _aff.Apply("s1", a.Id, AffiliateAction.Approve); return a;
    }
    int _n;
    async Task<Commission> Sale(Affiliate a, string? buyer = null)
    {
        var r = await _engine.CreditSettledInvoice(new("s1", "inv" + (++_n), 10m, "USD", buyer ?? $"b{_n}@x.io", a.Code, "p", null, "USD"), _ => Task.FromResult<decimal?>(50_000m));
        Assert.Equal(CreditOutcome.Credited, r.Outcome); return r.Commission!; // 4,000 sats each
    }
    async Task<PayoutBatch[]> Batches(string cycleId) { await using var db = _f.CreateDbContext(); return await db.PayoutBatches.Where(b => b.CycleId == cycleId).OrderBy(b => b.AmountSats).ToArrayAsync(); }
    async Task<CommissionStatus[]> Statuses(string affId) { await using var db = _f.CreateDbContext(); return await db.Commissions.Where(c => c.AffiliateId == affId).Select(c => c.Status).ToArrayAsync(); }

    [Fact]
    public async Task ManualCycleGroupsDueBalancesAndCarriesOverTheRest()
    {
        if (!_ok) Assert.Skip("dev Postgres not running");
        await Settings();
        var ln = await Active("ln@x.io"); var oc = await Active("oc@x.io", PayoutMethodKind.OnChain); var small = await Active("small@x.io");
        await Sale(ln); await Sale(ln);                    // 8,000 -> due
        var paid = await Sale(oc); await Sale(oc); await Sale(oc); await Sale(oc); // 12,000 payable + 4,000 paid, clawback -4,000 -> 8,000
        await Sale(small);                                  // 4,000 < 5,000 -> carried over
        await _ledger.MatureDue("s1");
        await using (var db = _f.CreateDbContext())
            await db.Commissions.Where(c => c.Id == paid.Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.Status, CommissionStatus.Paid));
        await _ledger.ReverseInvoice("s1", paid.InvoiceId, null, "Invoice refunded", "refund:x"); // -4,000 clawback
        var r = await _cycles.RunCycle("s1", manual: false);
        Assert.Equal(CycleRunOutcome.Created, r.Outcome);
        Assert.Equal(("M-2026-10-01", 2, 4_000L + 4_000L + 4_000L + 4_000L, 1, 4_000L),
            (r.Cycle!.CycleKey, r.Cycle.BatchCount, r.Cycle.TotalSats, r.Cycle.CarriedOverCount, r.Cycle.CarriedOverSats));
        var bs = await Batches(r.Cycle.Id);
        Assert.All(bs, b => Assert.Equal(PayoutBatchState.Draft, b.State)); // manual approval is the default
        Assert.Empty(_gw.Calls);
        var ocb = bs.Single(b => b.AffiliateId == oc.Id);
        Assert.Equal((PayoutMethodKind.OnChain, 3, 12_000L, -4_000L, 8_000L), (ocb.PayoutMethod, ocb.CommissionCount, ocb.GrossSats, ocb.AdjustmentSats, ocb.AmountSats));
        // batched items leave the open balance; scheduled cycle runs once per boundary
        Assert.DoesNotContain(await _ledger.Balances("s1"), b => b.AffiliateId == ln.Id);
        Assert.Equal(CycleRunOutcome.AlreadyRan, (await _cycles.RunCycle("s1", manual: false)).Outcome);
        Assert.Equal(0, await _cycles.RunScheduled());
        _clock.Now = new DateTimeOffset(2026, 11, 1, 0, 5, 0, TimeSpan.Zero);
        Assert.Equal(1, await _cycles.RunScheduled()); // next month
    }

    [Fact]
    public async Task ApproveCreatesPayoutsAndCompletionMarksPaid()
    {
        if (!_ok) Assert.Skip("dev Postgres not running");
        await Settings();
        var ln = await Active("ln@x.io"); var oc = await Active("oc@x.io", PayoutMethodKind.OnChain);
        await Sale(ln); await Sale(ln); await Sale(oc); await Sale(oc);
        var cycle = (await _cycles.RunCycle("s1", manual: true)).Cycle!;
        var bs = await Batches(cycle.Id);
        foreach (var b in bs) Assert.Equal(ApproveOutcome.Approved, (await _cycles.Approve("s1", b.Id)).Outcome);
        Assert.Equal(ApproveOutcome.WrongState, (await _cycles.Approve("s1", bs[0].Id)).Outcome); // no double payout
        Assert.Equal(2, _gw.Calls.Count);
        Assert.Contains(_gw.Calls, c => c.m == PayoutMethodKind.Lightning && c.dest == "x@ln.example" && c.sats == 8_000);
        Assert.Contains(_gw.Calls, c => c.m == PayoutMethodKind.OnChain && c.sats == 8_000);
        bs = await Batches(cycle.Id);
        Assert.All(bs, b => Assert.Equal((PayoutBatchState.Approved, true), (b.State, b.PayoutId is not null)));
        var lnb = bs.Single(b => b.AffiliateId == ln.Id);
        await _cycles.OnPayoutState(lnb.PayoutId!, PayoutState.InProgress);
        Assert.Equal(PayoutBatchState.Sent, (await Batches(cycle.Id)).Single(b => b.Id == lnb.Id).State);
        await _cycles.OnPayoutState(lnb.PayoutId!, PayoutState.Completed);
        Assert.All(await Statuses(ln.Id), s => Assert.Equal(CommissionStatus.Paid, s));
        Assert.All(await Statuses(oc.Id), s => Assert.Equal(CommissionStatus.Payable, s));
        Assert.Equal(new[] { lnb.PayoutId! }.Length, 1);
        Assert.DoesNotContain(lnb.PayoutId!, await _cycles.OpenPayoutIds("s1"));
        Assert.Contains(bs.Single(b => b.AffiliateId == oc.Id).PayoutId!, await _cycles.OpenPayoutIds("s1"));
        // a refund after payout now creates a clawback for the next cycle
        await using var db = _f.CreateDbContext();
        var inv = await db.Commissions.Where(c => c.AffiliateId == ln.Id).Select(c => c.InvoiceId).FirstAsync();
        Assert.Equal(ReversalOutcome.ClawedBack, (await _ledger.ReverseInvoice("s1", inv, null, "Invoice refunded", "refund:after")).Outcome);
        Assert.Equal(-4_000L, (await _ledger.Balances("s1")).Single(b => b.AffiliateId == ln.Id).NetSats);
    }

    [Fact]
    public async Task AutoApprovalAndFailureAndCancellationReleaseBalances()
    {
        if (!_ok) Assert.Skip("dev Postgres not running");
        await Settings(auto: true);
        var a = await Active("a@x.io");
        await Sale(a); await Sale(a);
        _gw.FailWith = "The LNURL / Lightning Address provided was not online.";
        var c1 = (await _cycles.RunCycle("s1", manual: true)).Cycle!;
        var b1 = (await Batches(c1.Id)).Single();
        Assert.Equal((PayoutBatchState.Failed, "The LNURL / Lightning Address provided was not online."), (b1.State, b1.Error));
        Assert.Equal(8_000L, (await _ledger.Balances("s1")).Single().NetSats); // back in the balance
        _gw.FailWith = null;
        var c2 = (await _cycles.RunCycle("s1", manual: true)).Cycle!;
        var b2 = (await Batches(c2.Id)).Single();
        Assert.Equal(PayoutBatchState.Approved, b2.State); // auto approved
        await _cycles.OnPayoutState(b2.PayoutId!, PayoutState.Cancelled); // cancelled in BTCPay
        Assert.Equal(PayoutBatchState.Cancelled, (await Batches(c2.Id)).Single().State);
        Assert.Equal(8_000L, (await _ledger.Balances("s1")).Single().NetSats);
        await _cycles.OnPayoutState(b2.PayoutId!, PayoutState.Completed); // late event ignored
        Assert.All(await Statuses(a.Id), s => Assert.Equal(CommissionStatus.Payable, s));
        // manual cancel of a draft
        await Settings(auto: false);
        var c3 = (await _cycles.RunCycle("s1", manual: true)).Cycle!;
        var b3 = (await Batches(c3.Id)).Single();
        Assert.True(await _cycles.Cancel("s1", b3.Id));
        Assert.False(await _cycles.Cancel("s1", b3.Id));
        Assert.Equal(8_000L, (await _ledger.Balances("s1")).Single().NetSats);
    }

    [Fact]
    public async Task DisabledMethodNewProgramAndSuspendedAffiliate()
    {
        if (!_ok) Assert.Skip("dev Postgres not running");
        var ln = await Active("ln@x.io"); var sus = await Active("sus@x.io", PayoutMethodKind.OnChain);
        await _prog.SaveSettings(new AffiliateStoreSettings { StoreId = "s1", HoldDays = 0, MinPayoutSats = 1, LightningEnabled = false, OnChainEnabled = true });
        await Sale(ln); await Sale(sus); await _aff.Apply("s1", sus.Id, AffiliateAction.Suspend);
        // program configured after the current boundary: the first scheduled cycle waits for the next boundary
        Assert.Equal(CycleRunOutcome.AlreadyRan, (await _cycles.RunCycle("s1", manual: false)).Outcome);
        var c = (await _cycles.RunCycle("s1", manual: true)).Cycle!;
        Assert.Equal(0, c.BatchCount); // LN disabled by the store, suspended affiliate held
        Assert.Equal(2, (await _ledger.Balances("s1")).Length);
    }
}
