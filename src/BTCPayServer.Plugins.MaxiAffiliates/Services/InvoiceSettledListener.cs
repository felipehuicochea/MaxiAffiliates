using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Plugins.MaxiAffiliates.Attribution;
using BTCPayServer.Plugins.Subscriptions;
using BTCPayServer.Rating;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.MaxiAffiliates.Services;

/// <summary>
/// Listens to BTCPay invoice events and credits commissions when an invoice is Settled.
/// Works for one-time invoices and Subscriptions invoices (initial + renewals: SUBS#checkout tag → plan checkout → plan / subscriber).
/// Uses core internals (InvoiceEvent, InvoiceEntity, PlanCheckoutData): re-verify on BTCPay upgrades.
/// </summary>
public class InvoiceSettledListener(
    EventAggregator events, ILogger<InvoiceSettledListener> logger, CommissionEngine engine,
    StoreRepository stores, ApplicationDbContextFactory appDb, RateFetcher rateFetcher, DefaultRulesCollection defaultRules)
    : EventHostedServiceBase(events, logger)
{
    protected override void SubscribeToEvents() => Subscribe<InvoiceEvent>();

    protected override async Task ProcessEvent(object evt, CancellationToken ct)
    {
        if (evt is not InvoiceEvent { Invoice: { } invoice } || invoice.Status != InvoiceStatus.Settled) return;
        try
        {
            var settled = await Map(invoice);
            if (settled is null) return;
            var result = await engine.CreditSettledInvoice(settled, cur => Rate(invoice, cur, ct), ct);
            if (result.Outcome != CreditOutcome.NoReferral)
                logger.LogInformation("MaxiAffiliates: invoice {Invoice} -> {Outcome}", invoice.Id, result.Outcome);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "MaxiAffiliates: failed to process settled invoice {Invoice}", invoice.Id);
        }
    }

    async Task<SettledInvoice?> Map(InvoiceEntity invoice)
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

    async Task<decimal?> Rate(InvoiceEntity invoice, string currency, CancellationToken ct)
    {
        var pair = new CurrencyPair("BTC", currency);
        if (invoice.TryGetRate(pair, out var r) && r > 0) return r; // settlement-time rate stored on the invoice
        var store = await stores.FindStore(invoice.StoreId);
        if (store is null) return null;
        var res = await rateFetcher.FetchRate(pair, store.GetStoreBlob().GetRateRules(defaultRules), null, ct);
        return res.BidAsk?.Bid is > 0 ? res.BidAsk.Bid : null;
    }
}
