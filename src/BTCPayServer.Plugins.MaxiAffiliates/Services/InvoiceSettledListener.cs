using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Plugins.Subscriptions;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.MaxiAffiliates.Services;

/// <summary>
/// Credits commissions when invoices settle, and reverses/claws back on invalidation, invoice refunds and subscription credit refunds.
/// </summary>
public class InvoiceSettledListener(
    EventAggregator events, ILogger<InvoiceSettledListener> logger, CommissionEngine engine, CommissionLedger ledger, InvoiceCommissionMapper mapper, PayoutCycleService cycles)
    : EventHostedServiceBase(events, logger)
{
    protected override void SubscribeToEvents()
    {
        Subscribe<InvoiceEvent>();
        Subscribe<SubscriptionEvent.CreditRefunded>();
        Subscribe<BTCPayServer.HostedServices.PayoutEvent>();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken ct)
    {
        try
        {
            switch (evt)
            {
                case InvoiceEvent { Name: InvoiceEvent.MarkedInvalid, Invoice: { } inv }:
                    Log(inv.Id, await ledger.ReverseInvoice(inv.StoreId, inv.Id, null, "Invoice marked invalid", $"invalid:{inv.Id}", ct));
                    break;
                case InvoiceEvent { Name: InvoiceEvent.Refund, Invoice: { } inv } e:
                {
                    // Sized from the refund pull payment when possible; otherwise treated as a full refund.
                    decimal? fraction = null;
                    var com = await ledger.FindByInvoice(inv.StoreId, inv.Id, ct);
                    if (com is not null && e.PullPaymentId is { } pp && await mapper.PullPaymentAmount(pp, ct) is { } amt)
                        fraction = CommissionLedger.RefundFraction(com, amt.amount, amt.currency);
                    Log(inv.Id, await ledger.ReverseInvoice(inv.StoreId, inv.Id, fraction, "Invoice refunded", $"refund:{e.PullPaymentId ?? inv.Id}", ct));
                    break;
                }
                case InvoiceEvent { Invoice: { Status: InvoiceStatus.Settled } inv }:
                    if (await mapper.Map(inv) is { } settled)
                    {
                        var result = await engine.CreditSettledInvoice(settled, cur => mapper.Rate(inv, cur, ct), ct);
                        if (result.Outcome != CreditOutcome.NoReferral)
                            logger.LogInformation("MaxiAffiliates: invoice {Invoice} -> {Outcome}", inv.Id, result.Outcome);
                    }
                    break;
                case BTCPayServer.HostedServices.PayoutEvent { Payout: { } po }:
                    await cycles.OnPayoutState(po.Id, po.State, ct);
                    break;
                case SubscriptionEvent.CreditRefunded r:
                {
                    var storeId = await mapper.StoreOfSubscriber(r.Subscriber.Id, ct);
                    if (storeId is null) break;
                    var com = await ledger.LatestForSubscriber(storeId, r.Subscriber.Id.ToString(), ct);
                    if (com is null) break;
                    var fraction = CommissionLedger.RefundFraction(com, r.Amount, r.Currency);
                    Log(com.InvoiceId, await ledger.ReverseCommission(storeId, com.Id, fraction, "Subscription credit refunded", $"credit-refund:{r.PullPaymentId}", ct));
                    break;
                }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "MaxiAffiliates: failed to process {Event}", evt.GetType().Name);
        }
    }

    void Log(string invoiceId, ReversalResult r)
    {
        if (r.Outcome != ReversalOutcome.NoCommission)
            logger.LogInformation("MaxiAffiliates: invoice {Invoice} reversal -> {Outcome} ({Sats} sats)", invoiceId, r.Outcome, r.ClawedSats);
    }
}
