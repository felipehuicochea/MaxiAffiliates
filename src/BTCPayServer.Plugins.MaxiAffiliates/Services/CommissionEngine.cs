using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.MaxiAffiliates.Attribution;
using BTCPayServer.Plugins.MaxiAffiliates.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.MaxiAffiliates.Services;

/// <summary>Provider-neutral view of a settled invoice (mapped from BTCPay's InvoiceEntity by the hosted service).</summary>
public record SettledInvoice(
    string StoreId, string InvoiceId, decimal Price, string Currency, string? BuyerEmail, string? Ref,
    string? PlanId, string? SubscriberId, string StoreDefaultCurrency);

public enum CreditOutcome { NoReferral, AffiliateInactive, SelfReferral, OutOfScope, AlreadyCredited, ZeroAmount, Credited, RateUnavailable }

public record CreditResult(CreditOutcome Outcome, Commission? Commission = null);

/// <summary>Resolves the rule (plan rule → store default rule → store settings) for a payment.</summary>
public record EffectiveRule(string? RuleId, CommissionType Type, decimal Value, CommissionScope Scope, int? FirstN);

public class CommissionEngine(IDbContextFactory<MaxiAffiliatesDbContext> dbFactory, TimeProvider clock, ILogger<CommissionEngine> logger)
{
    public static string CustomerKeyFor(SettledInvoice inv) =>
        !string.IsNullOrWhiteSpace(inv.BuyerEmail) ? "email:" + inv.BuyerEmail.Trim().ToLowerInvariant() : "invoice:" + inv.InvoiceId;

    public static string BaseCurrency(AffiliateStoreSettings s, string storeDefault) =>
        string.IsNullOrWhiteSpace(s.BaseCurrencyOverride) ? storeDefault.ToUpperInvariant() : s.BaseCurrencyOverride.Trim().ToUpperInvariant();

    public static EffectiveRule PickRule(AffiliateStoreSettings s, CommissionRule[] rules, string? planId)
    {
        var r = (planId is null ? null : rules.FirstOrDefault(x => x.Enabled && x.PlanId == planId))
                ?? rules.FirstOrDefault(x => x.Enabled && x.PlanId == null);
        return r is not null
            ? new(r.Id, r.Type, r.Value, r.Scope, r.FirstN)
            : new(null, CommissionType.Percent, s.DefaultPercent, s.DefaultScope, s.DefaultFirstN);
    }

    /// <param name="rates">Returns BTC→currency rate (units per BTC) or null if unavailable.</param>
    public async Task<CreditResult> CreditSettledInvoice(SettledInvoice inv, Func<string, Task<decimal?>> rates, CancellationToken ct = default)
    {
        var result = await CreditCore(inv, rates, ct);
        await TrackRetry(inv, result.Outcome, ct);
        return result;
    }

    /// <summary>Backoff for rate retries: 5 min, 10, 20 … capped at 6 h; give up after 7 days.</summary>
    public static TimeSpan RetryDelay(int attempts) => TimeSpan.FromMinutes(Math.Min(5 * Math.Pow(2, Math.Max(attempts - 1, 0)), 360));
    public static readonly TimeSpan RetryGiveUpAfter = TimeSpan.FromDays(7);

    async Task TrackRetry(SettledInvoice inv, CreditOutcome outcome, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var p = await db.PendingCredits.FirstOrDefaultAsync(x => x.StoreId == inv.StoreId && x.InvoiceId == inv.InvoiceId, ct);
        if (outcome != CreditOutcome.RateUnavailable)
        {
            if (p is null) return;
            db.PendingCredits.Remove(p);
        }
        else
        {
            var now = clock.GetUtcNow();
            if (p is null) { p = new PendingCredit { StoreId = inv.StoreId, InvoiceId = inv.InvoiceId, FirstSeenAt = now }; db.PendingCredits.Add(p); }
            else if (now < p.NextAttemptAt) return; // several settle events for one invoice: count one attempt
            p.Attempts++;
            p.LastError = $"No BTC rate for {inv.Currency} or the commission base currency";
            p.NextAttemptAt = now + RetryDelay(p.Attempts);
            p.GaveUp = now - p.FirstSeenAt >= RetryGiveUpAfter;
        }
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { /* concurrent tracker, harmless */ }
    }

    async Task<CreditResult> CreditCore(SettledInvoice inv, Func<string, Task<decimal?>> rates, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Commissions.AnyAsync(c => c.StoreId == inv.StoreId && c.InvoiceId == inv.InvoiceId, ct))
            return new(CreditOutcome.AlreadyCredited);
        var settings = await db.StoreSettings.AsNoTracking().FirstOrDefaultAsync(s => s.StoreId == inv.StoreId, ct)
                       ?? new AffiliateStoreSettings { StoreId = inv.StoreId };
        if (!settings.Enabled) return new(CreditOutcome.NoReferral);

        var customerKey = CustomerKeyFor(inv);
        var referral = await db.Referrals.Include(r => r.Affiliate)
            .FirstOrDefaultAsync(r => r.StoreId == inv.StoreId && r.CustomerKey == customerKey, ct);
        var now = clock.GetUtcNow();
        if (referral is null)
        {
            // New binding only from an explicit ref on the invoice (set by the plugin checkout or an integrator).
            if (!ReferralCookie.IsWellFormedCode(inv.Ref)) return new(CreditOutcome.NoReferral);
            var code = inv.Ref!.Trim().ToUpperInvariant();
            var aff = await db.Affiliates.FirstOrDefaultAsync(a => a.StoreId == inv.StoreId && a.Code == code, ct);
            if (aff is null || aff.Status != AffiliateStatus.Active) return new(CreditOutcome.AffiliateInactive);
            if (!string.IsNullOrWhiteSpace(inv.BuyerEmail) && string.Equals(inv.BuyerEmail.Trim(), aff.Email, StringComparison.OrdinalIgnoreCase))
                return new(CreditOutcome.SelfReferral);
            referral = new Referral
            {
                StoreId = inv.StoreId, AffiliateId = aff.Id, Affiliate = aff, CustomerKey = customerKey,
                SubscriberId = inv.SubscriberId, FirstInvoiceId = inv.InvoiceId, Source = ReferralSource.Api, AttributedAt = now
            };
            db.Referrals.Add(referral);
        }
        else if (referral.Affiliate.Status != AffiliateStatus.Active)
        {
            // Customer stays bound (first click), but suspended/banned affiliates earn nothing while inactive.
            return new(CreditOutcome.AffiliateInactive);
        }
        referral.SubscriberId ??= inv.SubscriberId;

        var rules = await db.CommissionRules.AsNoTracking().Where(r => r.StoreId == inv.StoreId).ToArrayAsync(ct);
        var rule = PickRule(settings, rules, inv.PlanId);
        if (!CommissionCalculator.InScope(rule.Scope, rule.FirstN, referral.CreditedPayments))
        {
            await db.SaveChangesAsync(ct); // persist a new binding even when this payment is out of scope
            return new(CreditOutcome.OutOfScope);
        }

        var baseCur = BaseCurrency(settings, inv.StoreDefaultCurrency);
        var rInv = CommissionCalculator.FixedRate(inv.Currency) ?? await rates(inv.Currency.ToUpperInvariant());
        var rBase = CommissionCalculator.FixedRate(baseCur) ?? await rates(baseCur);
        if (rInv is null or <= 0 || rBase is null or <= 0)
        {
            logger.LogWarning("MaxiAffiliates: no BTC rate for {Inv}/{Base}; invoice {Invoice} not credited", inv.Currency, baseCur, inv.InvoiceId);
            await db.SaveChangesAsync(ct);
            return new(CreditOutcome.RateUnavailable);
        }
        var q = CommissionCalculator.Compute(inv.Price, inv.Currency, baseCur, rInv.Value, rBase.Value, rule.Type, rule.Value);
        if (q.AmountSats <= 0)
        {
            await db.SaveChangesAsync(ct);
            return new(CreditOutcome.ZeroAmount);
        }
        var c = new Commission
        {
            StoreId = inv.StoreId, AffiliateId = referral.AffiliateId, Referral = referral, InvoiceId = inv.InvoiceId,
            RuleId = rule.RuleId, PlanId = inv.PlanId, SaleAmount = inv.Price, SaleCurrency = inv.Currency.ToUpperInvariant(), BaseAmount = q.BaseAmount, BaseCurrency = baseCur,
            CommissionAmount = q.CommissionAmount, AmountSats = q.AmountSats, Rate = q.RateBtcBase,
            Status = CommissionStatus.Pending, CreatedAt = now, PayableAt = now.AddDays(Math.Max(settings.HoldDays, 0))
        };
        db.Commissions.Add(c);
        referral.CreditedPayments++;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            if (await AlreadyCredited(inv, ct)) return new(CreditOutcome.AlreadyCredited);
            throw;
        }
        return new(CreditOutcome.Credited, c);
    }

    async Task<bool> AlreadyCredited(SettledInvoice inv, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Commissions.AnyAsync(c => c.StoreId == inv.StoreId && c.InvoiceId == inv.InvoiceId, ct);
    }
}
