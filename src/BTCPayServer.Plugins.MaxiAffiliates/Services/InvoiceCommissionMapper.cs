using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Plugins.MaxiAffiliates.Attribution;
using BTCPayServer.Plugins.Subscriptions;
using BTCPayServer.Rating;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.MaxiAffiliates.Services;

/// <summary>
/// Maps BTCPay invoices to <see cref="SettledInvoice"/> and supplies BTC rates.
/// Uses core internals (InvoiceEntity, SUBS# tag, PlanCheckoutData): re-verify on BTCPay upgrades.
/// </summary>
public class InvoiceCommissionMapper(StoreRepository stores, ApplicationDbContextFactory appDb, RateFetcher rateFetcher, DefaultRulesCollection defaultRules)
{
    public async Task<SettledInvoice?> Map(InvoiceEntity invoice)
    {
        var store = await stores.FindStore(invoice.StoreId);
        if (store is null) return null;
        var meta = invoice.Metadata.ToJObject();
        string? planId = meta["planId"]?.ToString();
        string? subscriberId = null;
        if (SubscriptionHostedService.GetCheckoutPlanIdFromInvoice(invoice) is { } checkoutId)
        {
            await using var ctx = appDb.CreateContext();
            var co = await ctx.PlanCheckouts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == checkoutId);
            if (co is not null)
            {
                planId ??= co.PlanId;
                subscriberId = co.SubscriberId?.ToString();
            }
        }
        planId ??= invoice.Metadata.ItemCode;
        return new SettledInvoice(invoice.StoreId, invoice.Id, invoice.Price, invoice.Currency, invoice.Metadata.BuyerEmail,
            meta[ReferralMetadata.RefKey]?.ToString(), planId, subscriberId, store.GetStoreBlob().DefaultCurrency ?? "USD");
    }

    /// <summary>BTC→currency: the rate stored on the invoice first, then the store's current rate rules.</summary>
    public async Task<decimal?> Rate(InvoiceEntity invoice, string currency, CancellationToken ct)
    {
        var pair = new CurrencyPair("BTC", currency);
        if (invoice.TryGetRate(pair, out var r) && r > 0) return r;
        var store = await stores.FindStore(invoice.StoreId);
        if (store is null) return null;
        var res = await rateFetcher.FetchRate(pair, store.GetStoreBlob().GetRateRules(defaultRules), null, ct);
        return res.BidAsk?.Bid is > 0 ? res.BidAsk.Bid : null;
    }

    public async Task<(decimal amount, string currency)?> PullPaymentAmount(string pullPaymentId, CancellationToken ct)
    {
        await using var ctx = appDb.CreateContext();
        var pp = await ctx.PullPayments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pullPaymentId, ct);
        return pp is null ? null : (pp.Limit, pp.Currency);
    }

    public async Task<System.Collections.Generic.Dictionary<string, BTCPayServer.Client.Models.PayoutState>> PayoutStates(string[] payoutIds, CancellationToken ct)
    {
        await using var ctx = appDb.CreateContext();
        return await ctx.Payouts.AsNoTracking().Where(p => payoutIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.State, ct);
    }

    public async Task<string?> StoreOfSubscriber(long subscriberId, CancellationToken ct)
    {
        await using var ctx = appDb.CreateContext();
        return await ctx.Subscribers.AsNoTracking().Where(s => s.Id == subscriberId).Select(s => s.Offering.App.StoreDataId).FirstOrDefaultAsync(ct);
    }
}
