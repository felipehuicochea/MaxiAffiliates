using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.MaxiAffiliates.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.MaxiAffiliates.Services;

public enum ReversalOutcome { NoCommission, AlreadyApplied, Reversed, ClawedBack, NothingLeft }
public record ReversalResult(ReversalOutcome Outcome, long ClawedSats = 0);

public enum BalanceState { Due, BelowThreshold, NegativeCarryOver, AffiliateInactive, Empty }
public record AffiliateBalance(string AffiliateId, string Email, string Code, AffiliateStatus Status,
    long PendingSats, long PayableSats, long AdjustmentSats, long NetSats, BalanceState State, DateTimeOffset? NextMaturity);

/// <summary>Hold period maturation, refund reversal/clawback, and payable balances with minimum threshold + carry-over.</summary>
public class CommissionLedger(IDbContextFactory<MaxiAffiliatesDbContext> dbFactory, TimeProvider clock, ILogger<CommissionLedger> logger)
{
    /// <summary>Pending → Payable for commissions whose hold period has elapsed.</summary>
    public async Task<int> MatureDue(string? storeId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow();
        var q = db.Commissions.Where(c => c.Status == CommissionStatus.Pending && c.PayableAt <= now);
        if (storeId is not null) q = q.Where(c => c.StoreId == storeId);
        return await q.ExecuteUpdateAsync(u => u.SetProperty(c => c.Status, CommissionStatus.Payable), ct);
    }

    /// <summary>
    /// Fraction of the sale being refunded, when it can be sized: refund in the sale currency, the base currency, or BTC.
    /// Null = unknown → treat as a full refund.
    /// </summary>
    public static decimal? RefundFraction(Commission c, decimal refundAmount, string refundCurrency)
    {
        if (refundAmount <= 0) return null;
        decimal? f = null;
        if (c.SaleAmount > 0 && string.Equals(refundCurrency, c.SaleCurrency, StringComparison.OrdinalIgnoreCase)) f = refundAmount / c.SaleAmount;
        else if (c.BaseAmount > 0 && string.Equals(refundCurrency, c.BaseCurrency, StringComparison.OrdinalIgnoreCase)) f = refundAmount / c.BaseAmount;
        else if (c.BaseAmount > 0 && c.Rate > 0 && CommissionCalculator.FixedRate(refundCurrency) is { } r)
            f = refundAmount / r / (c.BaseAmount / c.Rate);
        return f is null ? null : Math.Min(f.Value, 1m);
    }

    /// <summary>
    /// Applies a refund/invalidation to the commission of an invoice. fraction null or ≥ 1 = full.
    /// Unpaid + full → Reversed. Paid, or partial → negative <see cref="CommissionAdjustment"/> (capped at what is left), netted later.
    /// Idempotent on sourceRef.
    /// </summary>
    public async Task<ReversalResult> ReverseInvoice(string storeId, string invoiceId, decimal? fraction, string reason, string sourceRef, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var c = await db.Commissions.FirstOrDefaultAsync(x => x.StoreId == storeId && x.InvoiceId == invoiceId, ct);
        return c is null ? new(ReversalOutcome.NoCommission) : await Reverse(db, c, fraction, reason, sourceRef, ct);
    }

    public async Task<ReversalResult> ReverseCommission(string storeId, string commissionId, decimal? fraction, string reason, string sourceRef, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var c = await db.Commissions.FirstOrDefaultAsync(x => x.StoreId == storeId && x.Id == commissionId, ct);
        return c is null ? new(ReversalOutcome.NoCommission) : await Reverse(db, c, fraction, reason, sourceRef, ct);
    }

    public async Task<Commission?> FindByInvoice(string storeId, string invoiceId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Commissions.AsNoTracking().FirstOrDefaultAsync(c => c.StoreId == storeId && c.InvoiceId == invoiceId, ct);
    }

    /// <summary>Latest commission credited for a subscriber (used for subscription credit refunds).</summary>
    public async Task<Commission?> LatestForSubscriber(string storeId, string subscriberId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Commissions.AsNoTracking()
            .Where(c => c.StoreId == storeId && c.Referral.SubscriberId == subscriberId && c.Status != CommissionStatus.Reversed)
            .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);
    }

    async Task<ReversalResult> Reverse(MaxiAffiliatesDbContext db, Commission c, decimal? fraction, string reason, string sourceRef, CancellationToken ct)
    {
        if (c.Status == CommissionStatus.Reversed || await db.CommissionAdjustments.AnyAsync(a => a.StoreId == c.StoreId && a.SourceRef == sourceRef, ct))
            return new(ReversalOutcome.AlreadyApplied);
        var already = await db.CommissionAdjustments.Where(a => a.CommissionId == c.Id).SumAsync(a => (long?)a.AmountSats, ct) ?? 0;
        var left = c.AmountSats + already; // adjustments are negative
        var full = fraction is null || fraction >= 1m;
        var now = clock.GetUtcNow();
        if (full && c.Status is CommissionStatus.Pending or CommissionStatus.Payable)
        {
            c.Status = CommissionStatus.Reversed; c.ReversedAt = now; c.ReversedReason = reason;
            await db.SaveChangesAsync(ct);
            logger.LogInformation("MaxiAffiliates: commission {Id} reversed ({Reason})", c.Id, reason);
            return new(ReversalOutcome.Reversed, left);
        }
        var claw = full ? left : Math.Min(left, (long)Math.Round(c.AmountSats * Math.Max(fraction!.Value, 0m), MidpointRounding.AwayFromZero));
        if (claw <= 0) return new(ReversalOutcome.NothingLeft);
        db.CommissionAdjustments.Add(new CommissionAdjustment
        {
            StoreId = c.StoreId, AffiliateId = c.AffiliateId, CommissionId = c.Id, AmountSats = -claw,
            Reason = reason, SourceRef = sourceRef, CreatedAt = now
        });
        if (full) { c.ReversedAt = now; c.ReversedReason = reason; }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return new(ReversalOutcome.AlreadyApplied); }
        logger.LogInformation("MaxiAffiliates: clawed back {Sats} sats from commission {Id} ({Reason})", claw, c.Id, reason);
        return new(ReversalOutcome.ClawedBack, claw);
    }

    /// <summary>Threshold rule: pay only when the net balance reaches the minimum; otherwise it carries over (also when negative).</summary>
    public static BalanceState Classify(long netSats, long minPayoutSats, AffiliateStatus status) =>
        status != AffiliateStatus.Active && netSats != 0 ? BalanceState.AffiliateInactive :
        netSats < 0 ? BalanceState.NegativeCarryOver :
        netSats == 0 ? BalanceState.Empty :
        netSats >= Math.Max(minPayoutSats, 1) ? BalanceState.Due : BalanceState.BelowThreshold;

    /// <summary>
    /// Unbatched balances per affiliate. Payable commissions count fully; clawbacks count once their commission is Payable or Paid
    /// (a clawback on a still-Pending commission waits with it). Pending is shown for information.
    /// </summary>
    public async Task<AffiliateBalance[]> Balances(string storeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.StoreSettings.AsNoTracking().FirstOrDefaultAsync(s => s.StoreId == storeId, ct) ?? new AffiliateStoreSettings { StoreId = storeId };
        var com = await db.Commissions.AsNoTracking()
            .Where(c => c.StoreId == storeId && c.PayoutBatchId == null && (c.Status == CommissionStatus.Pending || c.Status == CommissionStatus.Payable))
            .GroupBy(c => new { c.AffiliateId, c.Status })
            .Select(g => new { g.Key.AffiliateId, g.Key.Status, Sats = g.Sum(c => c.AmountSats), Next = g.Min(c => c.PayableAt) }).ToListAsync(ct);
        var adj = await db.CommissionAdjustments.AsNoTracking()
            .Where(a => a.StoreId == storeId && a.PayoutBatchId == null &&
                        (a.Commission.Status == CommissionStatus.Payable || a.Commission.Status == CommissionStatus.Paid))
            .GroupBy(a => a.AffiliateId).Select(g => new { AffiliateId = g.Key, Sats = g.Sum(a => a.AmountSats) }).ToListAsync(ct);
        var ids = com.Select(x => x.AffiliateId).Concat(adj.Select(x => x.AffiliateId)).Distinct().ToList();
        var affs = await db.Affiliates.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        return ids.Where(affs.ContainsKey).Select(id =>
        {
            var a = affs[id];
            var pending = com.Where(x => x.AffiliateId == id && x.Status == CommissionStatus.Pending).Sum(x => x.Sats);
            var payable = com.Where(x => x.AffiliateId == id && x.Status == CommissionStatus.Payable).Sum(x => x.Sats);
            var adjust = adj.Where(x => x.AffiliateId == id).Sum(x => x.Sats);
            var next = com.Where(x => x.AffiliateId == id && x.Status == CommissionStatus.Pending).Select(x => (DateTimeOffset?)x.Next).Min();
            return new AffiliateBalance(id, a.Email, a.Code, a.Status, pending, payable, adjust, payable + adjust,
                Classify(payable + adjust, settings.MinPayoutSats, a.Status), next);
        }).OrderBy(b => b.State).ThenByDescending(b => b.NetSats).ToArray();
    }

    public async Task<PendingCredit[]> DueRetries(int take = 50, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow();
        return await db.PendingCredits.AsNoTracking().Where(p => !p.GaveUp && p.NextAttemptAt <= now).OrderBy(p => p.NextAttemptAt).Take(take).ToArrayAsync(ct);
    }

    public async Task<PendingCredit[]> ListPendingCredits(string storeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PendingCredits.AsNoTracking().Where(p => p.StoreId == storeId).OrderBy(p => p.FirstSeenAt).ToArrayAsync(ct);
    }

    /// <summary>Admin "retry now": make all of the store's waiting credits (including given-up ones) due immediately.</summary>
    public async Task<int> MakeRetriesDue(string storeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow();
        return await db.PendingCredits.Where(p => p.StoreId == storeId)
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.NextAttemptAt, now).SetProperty(p => p.GaveUp, false).SetProperty(p => p.FirstSeenAt, now), ct);
    }

    public async Task<Dictionary<string, long>> AdjustmentTotals(string storeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.CommissionAdjustments.AsNoTracking().Where(a => a.StoreId == storeId)
            .GroupBy(a => a.CommissionId).Select(g => new { g.Key, S = g.Sum(a => a.AmountSats) }).ToDictionaryAsync(x => x.Key, x => x.S, ct);
    }
}
