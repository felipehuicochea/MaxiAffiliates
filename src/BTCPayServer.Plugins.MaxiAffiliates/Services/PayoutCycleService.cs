using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.MaxiAffiliates.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.MaxiAffiliates.Services;

public enum CycleRunOutcome { Created, AlreadyRan, Disabled }
public record CycleRunResult(CycleRunOutcome Outcome, PayoutCycle? Cycle = null);
public enum ApproveOutcome { Approved, NotFound, WrongState, Failed }
public record ApproveResult(ApproveOutcome Outcome, string? Error = null);

/// <summary>
/// Weekly/monthly payout cycle: groups each affiliate's Due balance (payable commissions + clawbacks, ≥ minimum) into a
/// <see cref="PayoutBatch"/>, then creates BTCPay payouts on approval (manual by default, automatic if enabled),
/// and follows BTCPay payout state to mark commissions Paid or release them on cancellation.
/// </summary>
public class PayoutCycleService(IDbContextFactory<MaxiAffiliatesDbContext> dbFactory, CommissionLedger ledger, IPayoutGateway gateway,
    TimeProvider clock, ILogger<PayoutCycleService> logger)
{
    /// <summary>
    /// Latest cycle boundary at or before now (UTC midnight). Weekly: PayoutDay = weekday 0 (Sunday)..6. Monthly: day 1..28.
    /// </summary>
    public static (string key, DateTimeOffset boundary) CurrentCycle(PayoutFrequency f, int payoutDay, DateTimeOffset now)
    {
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        if (f == PayoutFrequency.Weekly)
        {
            var wd = Math.Clamp(payoutDay, 0, 6);
            var back = ((int)today.DayOfWeek - wd + 7) % 7;
            var b = today.AddDays(-back);
            return ($"W-{b:yyyy-MM-dd}", b);
        }
        var d = Math.Clamp(payoutDay, 1, 28);
        var m = new DateTimeOffset(today.Year, today.Month, d, 0, 0, 0, TimeSpan.Zero);
        if (m > today) m = m.AddMonths(-1);
        return ($"M-{m:yyyy-MM-dd}", m);
    }

    /// <summary>Runs the scheduled cycle for every enabled store whose current boundary has not run yet.</summary>
    public async Task<int> RunScheduled(CancellationToken ct = default)
    {
        string[] stores;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            stores = await db.StoreSettings.AsNoTracking().Where(s => s.Enabled).Select(s => s.StoreId).ToArrayAsync(ct);
        var created = 0;
        foreach (var s in stores)
            if ((await RunCycle(s, manual: false, ct)).Outcome == CycleRunOutcome.Created) created++;
        return created;
    }

    public async Task<CycleRunResult> RunCycle(string storeId, bool manual, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.StoreSettings.AsNoTracking().FirstOrDefaultAsync(s => s.StoreId == storeId, ct);
        if (settings is null || !settings.Enabled) return new(CycleRunOutcome.Disabled);
        var now = clock.GetUtcNow();
        var (key, boundary) = CurrentCycle(settings.PayoutFrequency, settings.PayoutDay, now);
        if (manual) { key = $"manual-{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..4]}"; boundary = now; }
        else if (settings.UpdatedAt > boundary && !await db.PayoutCycles.AnyAsync(c => c.StoreId == storeId, ct))
        {
            // A program configured after the current boundary starts at the next boundary.
            return new(CycleRunOutcome.AlreadyRan);
        }
        if (await db.PayoutCycles.AnyAsync(c => c.StoreId == storeId && c.CycleKey == key, ct)) return new(CycleRunOutcome.AlreadyRan);

        // Make sure holds that ended before the run are counted.
        await ledger.MatureDue(storeId, ct);
        var balances = await ledger.Balances(storeId, ct);
        var cycle = new PayoutCycle
        {
            StoreId = storeId, CycleKey = key, PeriodEnd = boundary, RunAt = now, Manual = manual,
            AutoApproved = settings.AutoApprovePayouts, MinPayoutSats = settings.MinPayoutSats,
            CarriedOverCount = balances.Count(b => b.State == BalanceState.BelowThreshold),
            CarriedOverSats = balances.Where(b => b.State == BalanceState.BelowThreshold).Sum(b => b.NetSats)
        };
        db.PayoutCycles.Add(cycle);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return new(CycleRunOutcome.AlreadyRan); }

        foreach (var bal in balances.Where(b => b.State == BalanceState.Due))
        {
            var batch = await CreateBatch(db, cycle, bal.AffiliateId, settings, ct);
            if (batch is null) continue;
            cycle.BatchCount++; cycle.TotalSats += batch.AmountSats;
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("MaxiAffiliates: payout cycle {Key} for store {Store}: {Count} batches, {Sats} sats", key, storeId, cycle.BatchCount, cycle.TotalSats);
        if (settings.AutoApprovePayouts)
            foreach (var id in await db.PayoutBatches.Where(b => b.CycleId == cycle.Id && b.State == PayoutBatchState.Draft).Select(b => b.Id).ToArrayAsync(ct))
                await Approve(storeId, id, ct);
        return new(CycleRunOutcome.Created, cycle);
    }

    async Task<PayoutBatch?> CreateBatch(MaxiAffiliatesDbContext db, PayoutCycle cycle, string affiliateId, AffiliateStoreSettings settings, CancellationToken ct)
    {
        var aff = await db.Affiliates.FirstAsync(a => a.Id == affiliateId, ct);
        if (aff.PayoutMethod == PayoutMethodKind.Lightning && !settings.LightningEnabled ||
            aff.PayoutMethod == PayoutMethodKind.OnChain && !settings.OnChainEnabled)
            return null; // method disabled by the store: keep the balance
        var batch = new PayoutBatch
        {
            StoreId = cycle.StoreId, AffiliateId = aff.Id, CycleId = cycle.Id, CycleStart = cycle.PeriodEnd, CycleEnd = cycle.PeriodEnd,
            PayoutMethod = aff.PayoutMethod, Destination = aff.PayoutDestination, State = PayoutBatchState.Draft, CreatedAt = cycle.RunAt
        };
        db.PayoutBatches.Add(batch);
        await db.SaveChangesAsync(ct);
        // Claim the items atomically (only unbatched ones), mirroring CommissionLedger.Balances.
        var commissions = await db.Commissions
            .Where(c => c.StoreId == cycle.StoreId && c.AffiliateId == aff.Id && c.PayoutBatchId == null && c.Status == CommissionStatus.Payable)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.PayoutBatchId, batch.Id), ct);
        await db.CommissionAdjustments
            .Where(a => a.StoreId == cycle.StoreId && a.AffiliateId == aff.Id && a.PayoutBatchId == null &&
                        (a.Commission.Status == CommissionStatus.Payable || a.Commission.Status == CommissionStatus.Paid))
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.PayoutBatchId, batch.Id), ct);
        batch.GrossSats = await db.Commissions.Where(c => c.PayoutBatchId == batch.Id).SumAsync(c => (long?)c.AmountSats, ct) ?? 0;
        batch.AdjustmentSats = await db.CommissionAdjustments.Where(a => a.PayoutBatchId == batch.Id).SumAsync(a => (long?)a.AmountSats, ct) ?? 0;
        batch.CommissionCount = commissions;
        batch.AmountSats = batch.GrossSats + batch.AdjustmentSats;
        if (batch.AmountSats < Math.Max(settings.MinPayoutSats, 1))
        {
            await Release(db, batch.Id, ct); // raced with another change; leave it for the next cycle
            db.PayoutBatches.Remove(batch);
            await db.SaveChangesAsync(ct);
            return null;
        }
        return batch;
    }

    static async Task Release(MaxiAffiliatesDbContext db, string batchId, CancellationToken ct)
    {
        await db.Commissions.Where(c => c.PayoutBatchId == batchId).ExecuteUpdateAsync(u => u.SetProperty(c => c.PayoutBatchId, (string?)null), ct);
        await db.CommissionAdjustments.Where(a => a.PayoutBatchId == batchId).ExecuteUpdateAsync(u => u.SetProperty(a => a.PayoutBatchId, (string?)null), ct);
    }

    /// <summary>Approval: creates the BTCPay payout (pre-approved). On failure the batch is Failed and its items return to the balance.</summary>
    public async Task<ApproveResult> Approve(string storeId, string batchId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var batch = await db.PayoutBatches.FirstOrDefaultAsync(b => b.StoreId == storeId && b.Id == batchId, ct);
        if (batch is null) return new(ApproveOutcome.NotFound);
        // Claim the transition first so a double click cannot create two payouts.
        var claimed = await db.PayoutBatches.Where(b => b.Id == batchId && b.State == PayoutBatchState.Draft)
            .ExecuteUpdateAsync(u => u.SetProperty(b => b.State, PayoutBatchState.Approved).SetProperty(b => b.ApprovedAt, clock.GetUtcNow()), ct);
        if (claimed == 0) return new(ApproveOutcome.WrongState);
        var meta = new JObject { ["source"] = "MaxiAffiliates", ["batchId"] = batch.Id, ["affiliateId"] = batch.AffiliateId };
        PayoutCreateResult res;
        try { res = await gateway.CreatePayout(storeId, batch.PayoutMethod, batch.Destination, batch.AmountSats, meta, ct); }
        catch (Exception e) when (e is not OperationCanceledException) { res = new(null, e.Message); }
        await db.Entry(batch).ReloadAsync(ct);
        if (res.PayoutId is null)
        {
            batch.State = PayoutBatchState.Failed; batch.Error = Truncate(res.Error, 512);
            await db.SaveChangesAsync(ct);
            await Release(db, batch.Id, ct);
            logger.LogWarning("MaxiAffiliates: payout for batch {Batch} failed: {Error}", batch.Id, res.Error);
            return new(ApproveOutcome.Failed, res.Error);
        }
        batch.PayoutId = res.PayoutId;
        await db.SaveChangesAsync(ct);
        return new(ApproveOutcome.Approved);
    }

    /// <summary>Cancel a batch that has not been sent to BTCPay yet; its items return to the balance.</summary>
    public async Task<bool> Cancel(string storeId, string batchId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var n = await db.PayoutBatches.Where(b => b.StoreId == storeId && b.Id == batchId && b.State == PayoutBatchState.Draft)
            .ExecuteUpdateAsync(u => u.SetProperty(b => b.State, PayoutBatchState.Cancelled), ct);
        if (n == 0) return false;
        await Release(db, batchId, ct);
        return true;
    }

    /// <summary>Follows BTCPay payout state. Completed → commissions Paid; Cancelled → items released to the balance.</summary>
    public async Task<PayoutBatch?> OnPayoutState(string payoutId, BTCPayServer.Client.Models.PayoutState state, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var batch = await db.PayoutBatches.FirstOrDefaultAsync(b => b.PayoutId == payoutId, ct);
        if (batch is null || batch.State is PayoutBatchState.Completed or PayoutBatchState.Cancelled or PayoutBatchState.Failed) return batch;
        var now = clock.GetUtcNow();
        switch (state)
        {
            case BTCPayServer.Client.Models.PayoutState.InProgress:
                batch.State = PayoutBatchState.Sent; break;
            case BTCPayServer.Client.Models.PayoutState.Completed:
                batch.State = PayoutBatchState.Completed; batch.CompletedAt = now;
                await db.Commissions.Where(c => c.PayoutBatchId == batch.Id && c.Status == CommissionStatus.Payable)
                    .ExecuteUpdateAsync(u => u.SetProperty(c => c.Status, CommissionStatus.Paid).SetProperty(c => c.PaidAt, now), ct);
                break;
            case BTCPayServer.Client.Models.PayoutState.Cancelled:
                batch.State = PayoutBatchState.Cancelled; batch.Error = "Payout cancelled in BTCPay";
                await Release(db, batch.Id, ct);
                break;
            default: return batch;
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("MaxiAffiliates: batch {Batch} payout {Payout} -> {State}", batch.Id, payoutId, batch.State);
        return batch;
    }

    /// <summary>Open batches with a BTCPay payout (Approved/Sent), to reconcile with BTCPay payout state (events can race the payout id being stored).</summary>
    public async Task<string[]> OpenPayoutIds(string? storeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PayoutBatches.AsNoTracking()
            .Where(b => (storeId == null || b.StoreId == storeId) && b.PayoutId != null && (b.State == PayoutBatchState.Approved || b.State == PayoutBatchState.Sent))
            .Select(b => b.PayoutId!).ToArrayAsync(ct);
    }

    public record BatchRow(PayoutBatch Batch, string Email, string Code, string? Name);

    public async Task<PayoutCycle[]> ListCycles(string storeId, int take = 50, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PayoutCycles.AsNoTracking().Where(c => c.StoreId == storeId).OrderByDescending(c => c.RunAt).Take(take).ToArrayAsync(ct);
    }

    public async Task<(PayoutCycle? cycle, BatchRow[] rows)> GetCycle(string storeId, string cycleId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var c = await db.PayoutCycles.AsNoTracking().FirstOrDefaultAsync(x => x.StoreId == storeId && x.Id == cycleId, ct);
        if (c is null) return (null, []);
        var rows = await db.PayoutBatches.AsNoTracking().Where(b => b.CycleId == c.Id).OrderByDescending(b => b.AmountSats)
            .Select(b => new BatchRow(b, b.Affiliate.Email, b.Affiliate.Code, b.Affiliate.Name)).ToArrayAsync(ct);
        return (c, rows);
    }

    /// <summary>"Commissions due" CSV for a cycle (RFC 4180, invariant culture).</summary>
    public static string ToCsv(PayoutCycle c, IEnumerable<BatchRow> rows)
    {
        var sb = new StringBuilder("cycle,period_end,affiliate_email,affiliate_name,affiliate_code,payout_method,destination,commissions,gross_sats,clawback_sats,amount_sats,amount_btc,state,payout_id,error\r\n");
        foreach (var r in rows)
        {
            var b = r.Batch;
            sb.AppendJoin(',', new[]
            {
                c.CycleKey, c.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), r.Email, r.Name ?? "", r.Code, b.PayoutMethod.ToString(), b.Destination,
                b.CommissionCount.ToString(CultureInfo.InvariantCulture), b.GrossSats.ToString(CultureInfo.InvariantCulture),
                b.AdjustmentSats.ToString(CultureInfo.InvariantCulture), b.AmountSats.ToString(CultureInfo.InvariantCulture),
                (b.AmountSats / 100_000_000m).ToString("0.00000000", CultureInfo.InvariantCulture), b.State.ToString(), b.PayoutId ?? "", b.Error ?? ""
            }.Select(Csv)).Append("\r\n");
        }
        return sb.ToString();
    }

    static string Csv(string v)
    {
        v = InputSafety.SpreadsheetSafe(v)!; // spreadsheet formula injection
        return v.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    static string? Truncate(string? s, int n) => s is null || s.Length <= n ? s : s[..n];
}
