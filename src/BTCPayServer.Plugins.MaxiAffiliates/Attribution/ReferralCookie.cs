using System;
using System.Globalization;

namespace BTCPayServer.Plugins.MaxiAffiliates.Attribution;

/// <summary>First-party cookie on the BTCPay host: value "CODE.unixSeconds" (time of the first click).</summary>
public static class ReferralCookie
{
    public static string Name(string storeId) => $"{AttributionDefaults.CookieName}_{storeId}";

    public static string Format(string code, DateTimeOffset seenAt) =>
        $"{code}.{seenAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}";

    public static (string? code, DateTimeOffset? seenAt) Parse(string? value)
    {
        if (string.IsNullOrEmpty(value)) return (null, null);
        var i = value.LastIndexOf('.');
        if (i <= 0 || !long.TryParse(value[(i + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var ts)) return (null, null);
        var code = value[..i];
        if (!IsWellFormedCode(code)) return (null, null);
        try { return (code, DateTimeOffset.FromUnixTimeSeconds(ts)); }
        catch (ArgumentOutOfRangeException) { return (null, null); }
    }

    public static bool IsWellFormedCode(string? code) =>
        !string.IsNullOrEmpty(code) && code.Length <= 32 && code.AsSpan().IndexOfAnyExcept("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcdefghijklmnopqrstuvwxyz-_") < 0;
}

/// <summary>Invoice / subscriber metadata contract for integrators.</summary>
public static class ReferralMetadata
{
    /// <summary>Put the affiliate code under this key in invoice metadata and (plan checkout) newSubscriberMetadata.</summary>
    public const string RefKey = "maxiaffRef";
    public const string AffiliateIdKey = "maxiaffAffiliateId";
}
