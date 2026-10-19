using System;

namespace BTCPayServer.Plugins.MaxiAffiliates.Attribution;

/// <summary>
/// Decides which referral code wins when a visitor has seen more than one.
/// v1 ships FIRST-CLICK only, with a fixed 30-day cookie (see <see cref="AttributionDefaults"/>).
/// A per-store first/last-click setting can later be added by registering another implementation.
/// </summary>
public interface IAttributionStrategy
{
    string Name { get; }
    TimeSpan CookieLifetime { get; }
    /// <summary>Returns the code to keep, given the code already stored (cookie/binding) and a newly seen one.</summary>
    string? Resolve(string? existingCode, DateTimeOffset? existingSeenAt, string? incomingCode, DateTimeOffset now);
}

public static class AttributionDefaults
{
    public const string CookieName = "maxiaff_ref";
    public static readonly TimeSpan CookieLifetime = TimeSpan.FromDays(30);
}

public sealed class FirstClickAttributionStrategy : IAttributionStrategy
{
    public string Name => "first-click";
    public TimeSpan CookieLifetime => AttributionDefaults.CookieLifetime;

    public string? Resolve(string? existingCode, DateTimeOffset? existingSeenAt, string? incomingCode, DateTimeOffset now)
    {
        var existingValid = !string.IsNullOrWhiteSpace(existingCode)
                            && existingSeenAt is { } seen && now - seen <= CookieLifetime;
        if (existingValid)
            return existingCode;
        return string.IsNullOrWhiteSpace(incomingCode) ? null : incomingCode.Trim().ToUpperInvariant();
    }
}
