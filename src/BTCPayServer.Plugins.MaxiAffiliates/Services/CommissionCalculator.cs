using System;
using BTCPayServer.Plugins.MaxiAffiliates.Data;

namespace BTCPayServer.Plugins.MaxiAffiliates.Services;

public record CommissionQuote(decimal BaseAmount, decimal CommissionAmount, long AmountSats, decimal RateBtcBase);

/// <summary>Pure commission maths. Rates are "units of currency per 1 BTC".</summary>
public static class CommissionCalculator
{
    public static CommissionQuote Compute(decimal invoicePrice, string invoiceCurrency, string baseCurrency,
        decimal rateBtcInvoice, decimal rateBtcBase, CommissionType type, decimal value)
    {
        if (invoicePrice < 0) throw new ArgumentOutOfRangeException(nameof(invoicePrice));
        rateBtcInvoice = FixedRate(invoiceCurrency) ?? rateBtcInvoice;
        rateBtcBase = FixedRate(baseCurrency) ?? rateBtcBase;
        if (rateBtcInvoice <= 0 || rateBtcBase <= 0) throw new ArgumentException("rates must be positive");
        var baseAmount = string.Equals(invoiceCurrency, baseCurrency, StringComparison.OrdinalIgnoreCase)
            ? invoicePrice
            : invoicePrice / rateBtcInvoice * rateBtcBase;
        baseAmount = Math.Round(baseAmount, 8);
        var commission = type switch
        {
            CommissionType.Percent => baseAmount * Math.Clamp(value, 0m, 100m) / 100m,
            CommissionType.Flat => Math.Min(Math.Max(value, 0m), baseAmount),
            _ => 0m
        };
        commission = Math.Round(commission, 8);
        var sats = (long)Math.Round(commission / rateBtcBase * 100_000_000m, MidpointRounding.ToZero);
        return new(baseAmount, commission, sats, rateBtcBase);
    }

    /// <summary>Rates that need no lookup: BTC = 1, SATS = 1e8 per BTC.</summary>
    public static decimal? FixedRate(string? c) =>
        string.Equals(c, "BTC", StringComparison.OrdinalIgnoreCase) ? 1m :
        string.Equals(c, "SATS", StringComparison.OrdinalIgnoreCase) ? 100_000_000m : null;

    public static bool IsBtc(string? c) => string.Equals(c, "BTC", StringComparison.OrdinalIgnoreCase) || string.Equals(c, "SATS", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the (n+1)-th credited payment of a referral earns commission under the scope.</summary>
    public static bool InScope(CommissionScope scope, int? firstN, int creditedSoFar) => scope switch
    {
        CommissionScope.FirstPayment => creditedSoFar == 0,
        CommissionScope.FirstN => creditedSoFar < Math.Max(firstN ?? 1, 1),
        CommissionScope.Lifetime => true,
        _ => false
    };
}
