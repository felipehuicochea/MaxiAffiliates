#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.MaxiAffiliates.Data;
using Microsoft.EntityFrameworkCore;
using NBitcoin;

namespace BTCPayServer.Plugins.MaxiAffiliates.Services;

public record PanelStats(long LinkClicks, int Referrals, int Sales, long EarnedSats, long PaidSats, long ReversedSats);

public record PanelOverview(Affiliate Affiliate, AffiliateStoreSettings Settings, PanelStats Stats, AffiliateBalance Balance,
    Commission[] Commissions, PayoutBatch[] Payouts);

public class AffiliatePanelService(IDbContextFactory<MaxiAffiliatesDbContext> dbFactory, CommissionLedger ledger, TimeProvider clock)
{
    public async Task<PanelOverview> Overview(Affiliate aff, int take = 50, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.StoreSettings.AsNoTracking().FirstOrDefaultAsync(s => s.StoreId == aff.StoreId, ct) ?? new AffiliateStoreSettings { StoreId = aff.StoreId };
        var mine = db.Commissions.AsNoTracking().Where(c => c.AffiliateId == aff.Id);
        var referrals = await db.Referrals.CountAsync(r => r.AffiliateId == aff.Id, ct);
        var sales = await mine.CountAsync(c => c.Status != CommissionStatus.Reversed, ct);
        var earned = await mine.Where(c => c.Status != CommissionStatus.Reversed).SumAsync(c => (long?)c.AmountSats, ct) ?? 0;
        var adjustments = await db.CommissionAdjustments.Where(a => a.AffiliateId == aff.Id).SumAsync(a => (long?)a.AmountSats, ct) ?? 0;
        var reversed = await mine.Where(c => c.Status == CommissionStatus.Reversed).SumAsync(c => (long?)c.AmountSats, ct) ?? 0;
        var paid = await db.PayoutBatches.Where(b => b.AffiliateId == aff.Id && b.State == PayoutBatchState.Completed).SumAsync(b => (long?)b.AmountSats, ct) ?? 0;
        var balance = (await ledger.Balances(aff.StoreId, ct)).FirstOrDefault(b => b.AffiliateId == aff.Id)
                      ?? new AffiliateBalance(aff.Id, aff.Email, aff.Code, aff.Status, 0, 0, 0, 0,
                          CommissionLedger.Classify(0, settings.MinPayoutSats, aff.Status), null);
        var commissions = await mine.OrderByDescending(c => c.CreatedAt).Take(take).ToArrayAsync(ct);
        var payouts = await db.PayoutBatches.AsNoTracking().Where(b => b.AffiliateId == aff.Id && b.State != PayoutBatchState.Cancelled)
            .OrderByDescending(b => b.CreatedAt).Take(take).ToArrayAsync(ct);
        return new(aff, settings, new PanelStats(aff.LinkClicks, referrals, sales, earned + adjustments, paid, reversed - adjustments), balance, commissions, payouts);
    }

    /// <summary>Validates and saves a new payout method/destination. Returns an error key or null.</summary>
    public async Task<string?> UpdatePayoutDestination(Affiliate aff, PayoutMethodKind method, string? destination, Network network, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.StoreSettings.AsNoTracking().FirstOrDefaultAsync(s => s.StoreId == aff.StoreId, ct) ?? new AffiliateStoreSettings { StoreId = aff.StoreId };
        if (method == PayoutMethodKind.Lightning && !settings.LightningEnabled || method == PayoutMethodKind.OnChain && !settings.OnChainEnabled)
            return "This payout method is not available.";
        var dest = PayoutDestinationValidator.Normalize(method, destination ?? "", network);
        if (dest is null)
            return method == PayoutMethodKind.Lightning ? "Enter a valid Lightning address (name@domain) or LNURL." : "Enter a valid Bitcoin address for this network.";
        var now = clock.GetUtcNow();
        await db.Affiliates.Where(a => a.Id == aff.Id).ExecuteUpdateAsync(u => u
            .SetProperty(a => a.PayoutMethod, method).SetProperty(a => a.PayoutDestination, dest)
            .SetProperty(a => a.PayoutDestinationUpdatedAt, now).SetProperty(a => a.UpdatedAt, now), ct);
        aff.PayoutMethod = method; aff.PayoutDestination = dest; aff.PayoutDestinationUpdatedAt = now;
        return null;
    }

    public async Task CountClick(string affiliateId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Affiliates.Where(a => a.Id == affiliateId).ExecuteUpdateAsync(u => u.SetProperty(a => a.LinkClicks, a => a.LinkClicks + 1), ct);
    }
}
