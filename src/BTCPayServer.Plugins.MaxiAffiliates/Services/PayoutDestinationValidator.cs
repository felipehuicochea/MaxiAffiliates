using System;
using System.Text.RegularExpressions;
using BTCPayServer.Plugins.MaxiAffiliates.Data;
using NBitcoin;

namespace BTCPayServer.Plugins.MaxiAffiliates.Services;

public static partial class PayoutDestinationValidator
{
    [GeneratedRegex(@"^[a-z0-9._+-]{1,64}@[a-z0-9-]+(\.[a-z0-9-]+)+$", RegexOptions.IgnoreCase)]
    private static partial Regex LightningAddressRegex();

    /// <summary>Returns a normalized destination, or null if invalid for the given method/network.</summary>
    public static string? Normalize(PayoutMethodKind kind, string? destination, Network network)
    {
        var d = destination?.Trim();
        if (string.IsNullOrEmpty(d) || d.Length > 512) return null;
        switch (kind)
        {
            case PayoutMethodKind.Lightning:
                if (LightningAddressRegex().IsMatch(d)) return d.ToLowerInvariant();
                if (d.StartsWith("lnurl1", StringComparison.OrdinalIgnoreCase) && d.Length > 20) return d.ToLowerInvariant();
                return null;
            case PayoutMethodKind.OnChain:
                try { return BitcoinAddress.Create(d, network).ToString(); }
                catch (FormatException) { return null; }
                catch (ArgumentException) { return null; }
            default:
                return null;
        }
    }
}
