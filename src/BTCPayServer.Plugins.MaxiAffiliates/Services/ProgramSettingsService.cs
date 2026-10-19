using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.MaxiAffiliates.Data;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.MaxiAffiliates.Services;

public record CommissionRow(Commission Commission, string AffiliateEmail, string AffiliateCode, string CustomerKey);

/// <summary>Program settings, per-plan commission rules and the commissions ledger (admin side).</summary>
public class ProgramSettingsService(IDbContextFactory<MaxiAffiliatesDbContext> dbFactory, TimeProvider clock)
{
    /// <summary>Validates and normalizes; returns an error message key or null.</summary>
    public static string? Validate(AffiliateStoreSettings s)
    {
        if (s.DefaultPercent is < 0 or > 100) return "The commission percentage must be between 0 and 100.";
        if (s.DefaultScope == CommissionScope.FirstN && s.DefaultFirstN is null or < 1) return "Enter how many payments earn a commission (N ≥ 1).";
        if (s.HoldDays is < 0 or > 365) return "The hold period must be between 0 and 365 days.";
        if (s.MinPayoutSats < 0) return "The minimum payout cannot be negative.";
        if (s.PayoutFrequency == PayoutFrequency.Weekly && s.PayoutDay is < 0 or > 6) return "For weekly payouts choose a weekday.";
        if (s.PayoutFrequency == PayoutFrequency.Monthly && s.PayoutDay is < 1 or > 28) return "For monthly payouts the day must be between 1 and 28.";
        if (!s.LightningEnabled && !s.OnChainEnabled) return "Enable at least one payout method.";
        if (!string.IsNullOrWhiteSpace(s.BaseCurrencyOverride) && !System.Text.RegularExpressions.Regex.IsMatch(s.BaseCurrencyOverride.Trim(), "^[A-Za-z]{3,5}$"))
            return "Enter a currency code such as USD, MXN or BTC, or leave it blank.";
        return null;
    }

    public static string? ValidateRule(CommissionRule r)
    {
        if (r.Value < 0) return "The commission value cannot be negative.";
        if (r.Type == CommissionType.Percent && r.Value > 100) return "The commission percentage must be between 0 and 100.";
        if (r.Scope == CommissionScope.FirstN && r.FirstN is null or < 1) return "Enter how many payments earn a commission (N ≥ 1).";
        return null;
    }

    public async Task SaveSettings(AffiliateStoreSettings input, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var s = await db.StoreSettings.FirstOrDefaultAsync(x => x.StoreId == input.StoreId, ct);
        if (s is null) { s = new AffiliateStoreSettings { StoreId = input.StoreId }; db.StoreSettings.Add(s); }
        s.Enabled = input.Enabled;
        s.DefaultPercent = input.DefaultPercent;
        s.DefaultScope = input.DefaultScope;
        s.DefaultFirstN = input.DefaultScope == CommissionScope.FirstN ? input.DefaultFirstN : null;
        s.HoldDays = input.HoldDays;
        s.LightningEnabled = input.LightningEnabled;
        s.OnChainEnabled = input.OnChainEnabled;
        s.AutoApproveAffiliates = input.AutoApproveAffiliates;
        s.AutoApprovePayouts = input.AutoApprovePayouts;
        s.MinPayoutSats = input.MinPayoutSats;
        s.PayoutFrequency = input.PayoutFrequency;
        s.PayoutDay = input.PayoutDay;
        s.BaseCurrencyOverride = string.IsNullOrWhiteSpace(input.BaseCurrencyOverride) ? null : input.BaseCurrencyOverride.Trim().ToUpperInvariant();
        s.LandingUrl = string.IsNullOrWhiteSpace(input.LandingUrl) ? null : input.LandingUrl.Trim();
        s.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    public async Task<CommissionRule[]> ListRules(string storeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.CommissionRules.AsNoTracking().Where(r => r.StoreId == storeId).OrderBy(r => r.PlanId).ThenBy(r => r.CreatedAt).ToArrayAsync(ct);
    }

    /// <summary>Adds or replaces the rule for (store, plan). One rule per plan; null plan = store-wide default rule.</summary>
    public async Task<CommissionRule> UpsertRule(CommissionRule input, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var planId = string.IsNullOrWhiteSpace(input.PlanId) ? null : input.PlanId.Trim();
        var r = await db.CommissionRules.FirstOrDefaultAsync(x => x.StoreId == input.StoreId && x.PlanId == planId, ct);
        if (r is null) { r = new CommissionRule { StoreId = input.StoreId, PlanId = planId, CreatedAt = clock.GetUtcNow() }; db.CommissionRules.Add(r); }
        r.Type = input.Type; r.Value = input.Value; r.Scope = input.Scope;
        r.FirstN = input.Scope == CommissionScope.FirstN ? input.FirstN : null;
        r.Enabled = input.Enabled;
        await db.SaveChangesAsync(ct);
        return r;
    }

    public async Task<bool> DeleteRule(string storeId, string ruleId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.CommissionRules.Where(r => r.StoreId == storeId && r.Id == ruleId).ExecuteDeleteAsync(ct) > 0;
    }

    public async Task<CommissionRow[]> ListCommissions(string storeId, CommissionStatus? status, int take = 200, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.Commissions.AsNoTracking().Where(c => c.StoreId == storeId);
        if (status is not null) q = q.Where(c => c.Status == status);
        return (await q.OrderByDescending(c => c.CreatedAt).Take(take)
            .Select(c => new { c, c.Affiliate.Email, c.Affiliate.Code, c.Referral.CustomerKey }).ToArrayAsync(ct))
            .Select(x => new CommissionRow(x.c, x.Email, x.Code, x.CustomerKey)).ToArray();
    }
}
