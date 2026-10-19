using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.MaxiAffiliates.Data;
using Microsoft.EntityFrameworkCore;
using NBitcoin;

namespace BTCPayServer.Plugins.MaxiAffiliates.Services;

public enum AffiliateAction { Approve, Suspend, Reactivate, Ban }

public record SignupRequest(string StoreId, string Email, string? Name, PayoutMethodKind PayoutMethod, string PayoutDestination);

/// <summary>Error keys are English source strings, translated in the UI via the BTCPay localizer.</summary>
public record SignupResult(Affiliate? Affiliate, string? ErrorField, string? ErrorKey)
{
    public bool Success => Affiliate is not null;
}

public class AffiliateService(IDbContextFactory<MaxiAffiliatesDbContext> dbFactory, TimeProvider clock)
{
    const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O/1/I

    public async Task<AffiliateStoreSettings> GetSettings(string storeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.StoreSettings.AsNoTracking().FirstOrDefaultAsync(s => s.StoreId == storeId, ct)
               ?? new AffiliateStoreSettings { StoreId = storeId };
    }

    public async Task<SignupResult> Signup(SignupRequest req, Network network, CancellationToken ct = default)
    {
        var email = req.Email?.Trim().ToLowerInvariant() ?? "";
        if (email.Length is < 3 or > 254 || !email.Contains('@') || email.StartsWith('@') || email.EndsWith('@'))
            return new(null, nameof(req.Email), "Enter a valid email address.");
        var settings = await GetSettings(req.StoreId, ct);
        if (!settings.Enabled)
            return new(null, null, "The affiliate program is not open for signups.");
        if (req.PayoutMethod == PayoutMethodKind.Lightning && !settings.LightningEnabled ||
            req.PayoutMethod == PayoutMethodKind.OnChain && !settings.OnChainEnabled)
            return new(null, nameof(req.PayoutMethod), "This payout method is not available.");
        var dest = PayoutDestinationValidator.Normalize(req.PayoutMethod, req.PayoutDestination, network);
        if (dest is null)
            return new(null, nameof(req.PayoutDestination), req.PayoutMethod == PayoutMethodKind.Lightning
                ? "Enter a valid Lightning address (name@domain) or LNURL."
                : "Enter a valid Bitcoin address for this network.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Affiliates.AnyAsync(a => a.StoreId == req.StoreId && a.Email == email, ct))
            return new(null, nameof(req.Email), "An affiliate with this email already exists.");

        var now = clock.GetUtcNow();
        var aff = new Affiliate
        {
            StoreId = req.StoreId, Email = email, Name = string.IsNullOrWhiteSpace(req.Name) ? null : req.Name.Trim()[..Math.Min(req.Name.Trim().Length, 100)],
            PayoutMethod = req.PayoutMethod, PayoutDestination = dest,
            Status = settings.AutoApproveAffiliates ? AffiliateStatus.Active : AffiliateStatus.Pending,
            CreatedAt = now, UpdatedAt = now
        };
        for (var attempt = 0; ; attempt++)
        {
            aff.Code = GenerateCode();
            db.Affiliates.Add(aff);
            try { await db.SaveChangesAsync(ct); return new(aff, null, null); }
            catch (DbUpdateException) when (attempt < 4)
            {
                db.ChangeTracker.Clear();
                if (await db.Affiliates.AnyAsync(a => a.StoreId == req.StoreId && a.Email == email, ct))
                    return new(null, nameof(req.Email), "An affiliate with this email already exists.");
            }
        }
    }

    public static string GenerateCode(int length = 8) =>
        new(Enumerable.Range(0, length).Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]).ToArray());

    /// <summary>Allowed status transitions. Banned is terminal.</summary>
    public static AffiliateStatus? Transition(AffiliateStatus from, AffiliateAction action) => (from, action) switch
    {
        (AffiliateStatus.Pending, AffiliateAction.Approve) => AffiliateStatus.Active,
        (AffiliateStatus.Active, AffiliateAction.Suspend) => AffiliateStatus.Suspended,
        (AffiliateStatus.Suspended, AffiliateAction.Reactivate) => AffiliateStatus.Active,
        (AffiliateStatus.Pending or AffiliateStatus.Active or AffiliateStatus.Suspended, AffiliateAction.Ban) => AffiliateStatus.Banned,
        _ => null
    };

    public async Task<(bool ok, Affiliate? affiliate)> Apply(string storeId, string affiliateId, AffiliateAction action, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var aff = await db.Affiliates.FirstOrDefaultAsync(a => a.StoreId == storeId && a.Id == affiliateId, ct);
        if (aff is null) return (false, null);
        var next = Transition(aff.Status, action);
        if (next is null) return (false, aff);
        aff.Status = next.Value;
        aff.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return (true, aff);
    }

    public async Task<Affiliate[]> List(string storeId, AffiliateStatus? status, string? search, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.Affiliates.AsNoTracking().Where(a => a.StoreId == storeId);
        if (status is { } s) q = q.Where(a => a.Status == s);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var t = search.Trim().ToLowerInvariant();
            q = q.Where(a => a.Email.Contains(t) || a.Code.ToLower().Contains(t) || (a.Name != null && a.Name.ToLower().Contains(t)));
        }
        return await q.OrderByDescending(a => a.CreatedAt).Take(500).ToArrayAsync(ct);
    }

    /// <summary>Referral links only resolve to Active affiliates (suspended/banned/pending are ignored).</summary>
    public async Task<Affiliate?> FindActiveByCode(string storeId, string code, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var c = code.Trim().ToUpperInvariant();
        return await db.Affiliates.AsNoTracking().FirstOrDefaultAsync(a => a.StoreId == storeId && a.Code == c && a.Status == AffiliateStatus.Active, ct);
    }
}
