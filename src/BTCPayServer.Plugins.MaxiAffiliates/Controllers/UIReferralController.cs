using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Plugins.MaxiAffiliates.Attribution;
using BTCPayServer.Plugins.MaxiAffiliates.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace BTCPayServer.Plugins.MaxiAffiliates.Controllers;

public class ReferralCheckoutRequest
{
    public string? OfferingId { get; set; }
    public string? PlanId { get; set; }
    public string? Ref { get; set; }
    public string? Email { get; set; }
    public string? SuccessRedirectUrl { get; set; }
}

/// <summary>Public referral endpoints: link redirect, website helper script, and referral-carrying plan checkout.</summary>
[Area(Plugin.Area)]
[AllowAnonymous]
public class UIReferralController(
    AffiliateService affiliates, ReferralService referrals, PlanCheckoutService checkouts, IAttributionStrategy strategy, AffiliatePanelService panel) : Controller
{
    /// <summary>Referral link. Records the click (first-click cookie) and redirects to the store's landing URL with ?ref=CODE.</summary>
    [HttpGet("~/aff/{storeId}/{code}")]
    public async Task<IActionResult> Click(string storeId, string code)
    {
        var settings = await affiliates.GetSettings(storeId, HttpContext.RequestAborted);
        if (!settings.Enabled || !TryGetLanding(settings.LandingUrl, out var landing))
            return NotFound();
        var cookieName = ReferralCookie.Name(storeId);
        var existing = Request.Cookies[cookieName];
        string? refForLanding = null;
        if (ReferralCookie.IsWellFormedCode(code) && await affiliates.FindActiveByCode(storeId, code, HttpContext.RequestAborted) is { } aff)
        {
            await panel.CountClick(aff.Id, HttpContext.RequestAborted);
            var newCookie = referrals.CookieAfterClick(existing, aff.Code);
            if (newCookie is not null)
                Response.Cookies.Append(cookieName, newCookie, CookieOptions());
            refForLanding = referrals.PickCode(newCookie ?? existing, null);
        }
        else
        {
            refForLanding = referrals.PickCode(existing, null); // keep an earlier valid first click
        }
        var target = refForLanding is null ? landing!.ToString() : QueryHelpers.AddQueryString(landing!.ToString(), "ref", refForLanding);
        return Redirect(target);
    }

    /// <summary>Website helper script (see docs/INTEGRATION.md).</summary>
    [HttpGet("~/plugins/maxiaffiliates/js/maxiaff.js")]
    public IActionResult Script()
    {
        var s = typeof(Plugin).Assembly.GetManifestResourceStream(
            Array.Find(typeof(Plugin).Assembly.GetManifestResourceNames(), n => n.EndsWith("Resources.js.maxiaff.js"))!)!;
        Response.Headers.CacheControl = "public, max-age=3600";
        Response.Headers["Access-Control-Allow-Origin"] = "*";
        return File(s, "application/javascript; charset=utf-8");
    }

    /// <summary>CORS is handled by BTCPay's global AllowAnyOrigin policy (no credentials). JSON (from the helper) or form/query (plain link). Creates a plan checkout carrying the referral and returns/redirects to it.</summary>
    [HttpPost("~/plugins/maxiaffiliates/{storeId}/checkout")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> CheckoutPost(string storeId)
    {
        // Parse the body ourselves: accept application/json and text/plain (CORS "simple request") alike.
        ReferralCheckoutRequest? req;
        try
        {
            using var reader = new System.IO.StreamReader(Request.Body);
            var text = await reader.ReadToEndAsync(HttpContext.RequestAborted);
            if (text.Length > 8192) return BadRequest(new { error = "body too large" });
            req = System.Text.Json.JsonSerializer.Deserialize<ReferralCheckoutRequest>(text, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (System.Text.Json.JsonException) { return BadRequest(new { error = "invalid JSON" }); }
        return await Checkout(storeId, req, json: true);
    }

    [HttpGet("~/plugins/maxiaffiliates/{storeId}/checkout")]
    public Task<IActionResult> CheckoutGet(string storeId, [FromQuery] ReferralCheckoutRequest req) => Checkout(storeId, req, json: false);

    async Task<IActionResult> Checkout(string storeId, ReferralCheckoutRequest? req, bool json)
    {
        if (req?.OfferingId is null || req.PlanId is null)
            return BadRequest(new { error = "offeringId and planId are required" });
        var res = await referrals.Resolve(storeId, Request.Cookies[ReferralCookie.Name(storeId)], req.Ref, req.Email, HttpContext.RequestAborted);
        var created = await checkouts.Create(storeId, req.OfferingId, req.PlanId, req.Email, req.SuccessRedirectUrl, res.Affiliate, Request.GetRequestBaseUrl());
        if (created is null)
            return NotFound(new { error = "plan not found" });
        if (!json)
            return Redirect(created.Url);
        return Json(new { checkoutId = created.CheckoutId, url = created.Url, attributed = res.Outcome == RefOutcome.Attributed });
    }

    public static bool TryGetLanding(string? url, out Uri? landing)
    {
        landing = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp)) return false;
        landing = u; return true;
    }

    CookieOptions CookieOptions() => new()
    {
        HttpOnly = true, IsEssential = true, Path = "/",
        Secure = Request.IsHttps,
        // Lax: sent on top-level navigations (no-JS checkout link). Cross-site fetches pass the ref in the body instead.
        SameSite = SameSiteMode.Lax,
        MaxAge = strategy.CookieLifetime
    };
}
