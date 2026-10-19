using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions;
using BTCPayServer.Plugins.MaxiAffiliates.Data;
using BTCPayServer.Data;
using Affiliate = BTCPayServer.Plugins.MaxiAffiliates.Data.Affiliate;
using BTCPayServer.Data.Subscriptions;
using BTCPayServer.Plugins.MaxiAffiliates.Attribution;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.MaxiAffiliates.Services;

/// <summary>
/// Creates a BTCPay Subscriptions plan checkout carrying the referral in invoice + new-subscriber metadata.
/// Mirrors Greenfield POST /api/v1/plan-checkout (core 2.4.5). Uses core data types: re-verify on BTCPay upgrades.
/// </summary>
public class PlanCheckoutService(ApplicationDbContextFactory dbFactory)
{
    public record Result(string CheckoutId, string Url);

    public async Task<Result?> Create(string storeId, string offeringId, string planId, string? email, string? successRedirectUrl,
        Affiliate? affiliate, RequestBaseUrl baseUrl)
    {
        await using var ctx = dbFactory.CreateContext();
        var plan = await ctx.Plans.GetPlanFromId(planId, offeringId, storeId);
        if (plan is null) return null;
        var meta = new JObject();
        if (affiliate is not null)
        {
            meta[ReferralMetadata.RefKey] = affiliate.Code;
            meta[ReferralMetadata.AffiliateIdKey] = affiliate.Id;
        }
        var data = new PlanCheckoutData
        {
            NewSubscriber = true,
            NewSubscriberEmail = InputSafety.NormalizeEmail(email),
            NewSubscriberMetadata = meta.ToString(Newtonsoft.Json.Formatting.None),
            InvoiceMetadata = meta.ToString(Newtonsoft.Json.Formatting.None),
            PlanId = plan.Id,
            SuccessRedirectUrl = Uri.IsWellFormedUriString(successRedirectUrl, UriKind.Absolute) &&
                                 (successRedirectUrl!.StartsWith("https://") || successRedirectUrl.StartsWith("http://")) ? successRedirectUrl : null,
            Metadata = "{}",
            BaseUrl = baseUrl
        };
        ctx.PlanCheckouts.Add(data);
        await ctx.SaveChangesAsync();
        return new Result(data.Id, baseUrl.GetUrl($"/plan-checkout/{data.Id}"));
    }
}
