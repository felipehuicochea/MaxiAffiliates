#nullable enable
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.MaxiAffiliates.Data;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.MaxiAffiliates.Services;

/// <summary>
/// Passwordless access to the affiliate panel. Magic links (emailed, single use, 15 min), admin-issued
/// links (fallback when the store cannot send email; reusable until expiry or reissue) and sessions
/// (HttpOnly cookie, 7 days). Tokens are 256-bit random; only their SHA-256 is stored.
/// </summary>
public class PanelAuthService(IDbContextFactory<MaxiAffiliatesDbContext> dbFactory, TimeProvider clock)
{
    public static readonly TimeSpan MagicLinkLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan AdminLinkLifetime = TimeSpan.FromDays(30);
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);
    public const int MaxMagicLinksPerWindow = 3;

    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    static bool CanUsePanel(AffiliateStatus s) => s is AffiliateStatus.Active or AffiliateStatus.Pending or AffiliateStatus.Suspended;

    /// <summary>Returns (affiliate, raw token) or null when no eligible affiliate exists or it is rate limited. Callers must not reveal which.</summary>
    public async Task<(Affiliate affiliate, string token)?> CreateMagicLink(string storeId, string email, CancellationToken ct = default)
    {
        email = (email ?? "").Trim().ToLowerInvariant();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var aff = await db.Affiliates.AsNoTracking().FirstOrDefaultAsync(a => a.StoreId == storeId && a.Email == email, ct);
        if (aff is null || !CanUsePanel(aff.Status)) return null;
        var now = clock.GetUtcNow();
        var since = now - MagicLinkLifetime;
        if (await db.AccessTokens.CountAsync(t => t.AffiliateId == aff.Id && t.Kind == AccessTokenKind.MagicLink && t.CreatedAt > since, ct) >= MaxMagicLinksPerWindow)
            return null;
        var raw = NewToken();
        db.AccessTokens.Add(new AffiliateAccessToken { StoreId = storeId, AffiliateId = aff.Id, Kind = AccessTokenKind.MagicLink, TokenHash = Hash(raw), CreatedAt = now, ExpiresAt = now + MagicLinkLifetime });
        await db.SaveChangesAsync(ct);
        return (aff, raw);
    }

    /// <summary>Admin-issued access link; revokes earlier admin links of this affiliate.</summary>
    public async Task<string?> IssueAdminLink(string storeId, string affiliateId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var aff = await db.Affiliates.AsNoTracking().FirstOrDefaultAsync(a => a.StoreId == storeId && a.Id == affiliateId, ct);
        if (aff is null || !CanUsePanel(aff.Status)) return null;
        var now = clock.GetUtcNow();
        await db.AccessTokens.Where(t => t.AffiliateId == aff.Id && t.Kind == AccessTokenKind.AdminLink && t.RevokedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.RevokedAt, now), ct);
        var raw = NewToken();
        db.AccessTokens.Add(new AffiliateAccessToken { StoreId = storeId, AffiliateId = aff.Id, Kind = AccessTokenKind.AdminLink, TokenHash = Hash(raw), CreatedAt = now, ExpiresAt = now + AdminLinkLifetime });
        await db.SaveChangesAsync(ct);
        return raw;
    }

    /// <summary>Exchanges a magic/admin link for a new session token. Magic links are consumed atomically.</summary>
    public async Task<string?> Redeem(string storeId, string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 100) return null;
        var hash = Hash(token);
        var now = clock.GetUtcNow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var t = await db.AccessTokens.Include(x => x.Affiliate).FirstOrDefaultAsync(x => x.TokenHash == hash && x.StoreId == storeId &&
            (x.Kind == AccessTokenKind.MagicLink || x.Kind == AccessTokenKind.AdminLink), ct);
        if (t is null || t.RevokedAt != null || t.ExpiresAt <= now || !CanUsePanel(t.Affiliate.Status)) return null;
        if (t.Kind == AccessTokenKind.MagicLink)
        {
            var claimed = await db.AccessTokens.Where(x => x.Id == t.Id && x.UsedAt == null).ExecuteUpdateAsync(u => u.SetProperty(x => x.UsedAt, now), ct);
            if (claimed == 0) return null;
        }
        else
        {
            t.UsedAt = now;
        }
        var raw = NewToken();
        db.AccessTokens.Add(new AffiliateAccessToken { StoreId = storeId, AffiliateId = t.AffiliateId, Kind = AccessTokenKind.Session, TokenHash = Hash(raw), CreatedAt = now, ExpiresAt = now + SessionLifetime });
        await db.SaveChangesAsync(ct);
        return raw;
    }

    public async Task<Affiliate?> Authenticate(string storeId, string? session, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(session) || session.Length > 100) return null;
        var hash = Hash(session);
        var now = clock.GetUtcNow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var t = await db.AccessTokens.AsNoTracking().Include(x => x.Affiliate)
            .FirstOrDefaultAsync(x => x.TokenHash == hash && x.StoreId == storeId && x.Kind == AccessTokenKind.Session, ct);
        if (t is null || t.RevokedAt != null || t.ExpiresAt <= now || !CanUsePanel(t.Affiliate.Status)) return null;
        return t.Affiliate;
    }

    public async Task Logout(string storeId, string? session, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(session) || session.Length > 100) return;
        var hash = Hash(session);
        var now = clock.GetUtcNow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.AccessTokens.Where(x => x.TokenHash == hash && x.StoreId == storeId && x.Kind == AccessTokenKind.Session)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.RevokedAt, now), ct);
    }

    /// <summary>Ends every session and link of an affiliate (used on ban and on payout address change, except the current session).</summary>
    public async Task RevokeAll(string affiliateId, string? exceptSession = null, CancellationToken ct = default)
    {
        var keep = exceptSession is null ? null : Hash(exceptSession);
        var now = clock.GetUtcNow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.AccessTokens.Where(x => x.AffiliateId == affiliateId && x.RevokedAt == null && x.TokenHash != keep)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.RevokedAt, now), ct);
    }
}
