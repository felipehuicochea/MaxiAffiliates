using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.HostedServices;
using BTCPayServer.Payouts;
using BTCPayServer.Plugins.MaxiAffiliates.Data;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.MaxiAffiliates.Services;

public record PayoutCreateResult(string? PayoutId, string? Error);

/// <summary>Creates store payouts. Abstracted so the cycle/approval logic is testable without a node.</summary>
public interface IPayoutGateway
{
    Task<PayoutCreateResult> CreatePayout(string storeId, PayoutMethodKind method, string destination, long sats, JObject metadata, CancellationToken ct);
}

/// <summary>
/// BTCPay implementation: a store payout (no pull payment), pre-approved so the store's payout processors
/// (on-chain / Lightning automated senders) or a manual send pay it. Same path as Greenfield POST /stores/{id}/payouts.
/// </summary>
public class BtcPayPayoutGateway(PayoutMethodHandlerDictionary handlers, PullPaymentHostedService pullPayments) : IPayoutGateway
{
    public static string PayoutMethodId(PayoutMethodKind m) => m == PayoutMethodKind.Lightning ? "BTC-LN" : "BTC-CHAIN";

    public async Task<PayoutCreateResult> CreatePayout(string storeId, PayoutMethodKind method, string destination, long sats, JObject metadata, CancellationToken ct)
    {
        var pmi = BTCPayServer.Payouts.PayoutMethodId.Parse(PayoutMethodId(method));
        if (!handlers.TryGetValue(pmi, out var handler)) return new(null, $"Payout method {pmi} is not available on this server.");
        var dest = await handler.ParseAndValidateClaimDestination(destination, null, ct);
        if (dest.destination is null) return new(null, dest.error ?? "Invalid payout destination.");
        var amount = sats / 100_000_000m;
        if (dest.destination.Amount is { } fixedAmount && fixedAmount != amount)
            return new(null, "The destination requests a fixed amount different from the payout amount.");
        var res = await pullPayments.Claim(new ClaimRequest
        {
            StoreId = storeId, PayoutMethodId = pmi, Destination = dest.destination, ClaimedAmount = amount,
            PreApprove = true, Metadata = metadata
        });
        return res.Result == ClaimRequest.ClaimResult.Ok
            ? new(res.PayoutData.Id, null)
            : new(null, ClaimRequest.GetErrorMessage(res.Result));
    }
}
