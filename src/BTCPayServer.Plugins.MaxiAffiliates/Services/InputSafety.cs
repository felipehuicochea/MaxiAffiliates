#nullable enable
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BTCPayServer.Plugins.MaxiAffiliates.Services;

/// <summary>Shared input/output hardening (M9 security review).</summary>
public static partial class InputSafety
{
    [GeneratedRegex(@"^[a-z0-9.!#$%&'*+/=?^_`{|}~-]{1,64}@[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+$")]
    private static partial Regex EmailRegex();

    /// <summary>Lower-cased, trimmed email or null when it is not a plain addr-spec (no display names, quotes, spaces, angle brackets).</summary>
    public static string? NormalizeEmail(string? email)
    {
        var e = email?.Trim().ToLowerInvariant();
        if (e is null || e.Length > 254 || !EmailRegex().IsMatch(e)) return null;
        return e;
    }

    /// <summary>Prefixes values a spreadsheet would run as a formula (=, +, -, @, tab, CR) with a quote. Numbers are left alone.</summary>
    public static string? SpreadsheetSafe(string? v)
    {
        if (string.IsNullOrEmpty(v)) return v;
        if ("=+-@\t\r".Contains(v[0]) && !decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out _)) return "'" + v;
        return v;
    }

    /// <summary>
    /// Post-payment redirect allowed only to the store's own sites: the landing page host or this BTCPay host, http(s) only.
    /// Anything else is dropped (BTCPay then shows its own receipt), so a referral checkout link cannot bounce buyers to a third-party site.
    /// </summary>
    public static string? SafeSuccessRedirect(string? url, string? landingUrl, string? btcpayHost)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 2000) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp) || !string.IsNullOrEmpty(u.UserInfo)) return null;
        var host = u.Host;
        if (Uri.TryCreate(landingUrl, UriKind.Absolute, out var l) && string.Equals(l.Host, host, StringComparison.OrdinalIgnoreCase)) return u.ToString();
        if (!string.IsNullOrEmpty(btcpayHost) && string.Equals(btcpayHost, host, StringComparison.OrdinalIgnoreCase)) return u.ToString();
        return null;
    }
}
