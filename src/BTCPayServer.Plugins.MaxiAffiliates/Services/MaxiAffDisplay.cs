using System.Globalization;
using BTCPayServer.Plugins.MaxiAffiliates.Data;
using BTCPayServer.Services.Rates;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Plugins.MaxiAffiliates.Services;

/// <summary>Display helpers: amounts rounded to the currency's decimals, friendly localizable labels.</summary>
public static class MaxiAffDisplay
{
    public static int Decimals(CurrencyNameTable table, string currency) =>
        CommissionCalculator.IsBtc(currency) ? (currency.ToUpperInvariant() == "SATS" ? 0 : 8)
        : table.GetCurrencyData(currency, true)?.Divisibility ?? 2;

    public static string Amount(CurrencyNameTable table, decimal amount, string currency)
    {
        var d = Decimals(table, currency);
        return System.Math.Round(amount, d, System.MidpointRounding.AwayFromZero).ToString("N" + d, CultureInfo.InvariantCulture) + " " + currency;
    }

    public static string ScopeKey(CommissionScope s) => s switch
    {
        CommissionScope.FirstPayment => "First payment only",
        CommissionScope.FirstN => "First N payments",
        _ => "Every payment (lifetime)"
    };

    public static string Scope(IStringLocalizer l, CommissionScope s, int? n) =>
        s == CommissionScope.FirstN && n is { } k ? l["First {0} payments", k] : l[ScopeKey(s)];

    public static string TypeKey(CommissionType t) => t == CommissionType.Flat ? "Flat amount" : "Percentage";

    public static string StateKey(BalanceState s) => s switch
    {
        BalanceState.Due => "Due for payout",
        BalanceState.BelowThreshold => "Below minimum, carries over",
        BalanceState.NegativeCarryOver => "Negative (clawback), carries over",
        BalanceState.AffiliateInactive => "Held: affiliate not active",
        _ => "Nothing payable"
    };
}
