using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.MaxiAffiliates.Attribution;
using BTCPayServer.Plugins.MaxiAffiliates.Data;

namespace BTCPayServer.Plugins.MaxiAffiliates.Services;

public enum RefOutcome { None, Attributed, UnknownOrInactive, SelfReferral }

public record RefResolution(RefOutcome Outcome, Affiliate? Affiliate, string? CandidateCode);

/// <summary>Turns the (cookie, explicit ref, buyer email) triple into an attributed affiliate, applying the attribution strategy and fraud rules.</summary>
public class ReferralService(AffiliateService affiliates, IAttributionStrategy strategy, TimeProvider clock)
{
    public string? PickCode(string? cookieValue, string? explicitRef)
    {
        var (cCode, cSeen) = ReferralCookie.Parse(cookieValue);
        var incoming = ReferralCookie.IsWellFormedCode(explicitRef?.Trim()) ? explicitRef!.Trim() : null;
        return strategy.Resolve(cCode?.ToUpperInvariant(), cSeen, incoming, clock.GetUtcNow());
    }

    public async Task<RefResolution> Resolve(string storeId, string? cookieValue, string? explicitRef, string? buyerEmail, CancellationToken ct = default)
    {
        var code = PickCode(cookieValue, explicitRef);
        if (code is null) return new(RefOutcome.None, null, null);
        var aff = await affiliates.FindActiveByCode(storeId, code, ct);
        if (aff is null) return new(RefOutcome.UnknownOrInactive, null, code);
        if (!string.IsNullOrWhiteSpace(buyerEmail) && string.Equals(buyerEmail.Trim(), aff.Email, StringComparison.OrdinalIgnoreCase))
            return new(RefOutcome.SelfReferral, null, code);
        return new(RefOutcome.Attributed, aff, code);
    }

    /// <summary>Cookie value to (re)write after a click, or null to leave the existing cookie unchanged.</summary>
    public string? CookieAfterClick(string? existingCookie, string clickedCode)
    {
        var (cCode, cSeen) = ReferralCookie.Parse(existingCookie);
        var now = clock.GetUtcNow();
        var winner = strategy.Resolve(cCode?.ToUpperInvariant(), cSeen, clickedCode, now);
        if (winner is null) return null;
        return winner == cCode?.ToUpperInvariant() && cSeen is { } seen ? null : ReferralCookie.Format(winner, now);
    }
}
