#nullable enable
using System;
using System.Net;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Emails.Services;
using BTCPayServer.Plugins.MaxiAffiliates.Data;
using BTCPayServer.Plugins.MaxiAffiliates.Models;
using BTCPayServer.Plugins.MaxiAffiliates.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using MimeKit;
using NicolasDorier.RateLimits;

namespace BTCPayServer.Plugins.MaxiAffiliates.Controllers;

/// <summary>Affiliate self-service panel. Not BTCPay users: passwordless magic link by email, or an admin-issued link.</summary>
[Area(Plugin.Area)]
[AllowAnonymous]
public class UIAffiliatePanelController(
    PanelAuthService auth, AffiliatePanelService panel, AffiliateService affiliates, StoreRepository stores,
    EmailSenderFactory emailSenders, BTCPayNetworkProvider networks, IStringLocalizer localizer,
    ILogger<UIAffiliatePanelController> logger) : Controller
{
    public const string CookieName = "maxiaff_panel";
    const string Base = "~/plugins/maxiaffiliates/{storeId}/panel";

    string CookiePath(string storeId) => $"{Request.PathBase}/plugins/maxiaffiliates/{storeId}/panel";

    [HttpGet(Base + "/login")]
    public async Task<IActionResult> Login(string storeId)
    {
        var vm = await LoginModel(storeId);
        if (vm is null) return NotFound();
        if (await Current(storeId) is not null) return RedirectToAction(nameof(Index), new { storeId });
        return View(vm);
    }

    [HttpPost(Base + "/login")]
    [ValidateAntiForgeryToken]
    [RateLimitsFilter(ZoneLimits.Login, Scope = RateLimitsScope.RemoteAddress)]
    public async Task<IActionResult> Login(string storeId, string? email)
    {
        var vm = await LoginModel(storeId);
        if (vm is null) return NotFound();
        if (!vm.EmailAvailable) return View(vm);
        vm.Email = email;
        if (InputSafety.NormalizeEmail(email) is null)
        {
            ModelState.AddModelError(nameof(email), localizer["Enter a valid email address."]);
            return View(vm);
        }
        var link = await auth.CreateMagicLink(storeId, email, HttpContext.RequestAborted);
        if (link is { } l)
        {
            var url = Url.Action(nameof(Auth), "UIAffiliatePanel", new { storeId, token = l.token, area = Plugin.Area }, Request.Scheme)!;
            var sender = await emailSenders.GetEmailSender(storeId);
            var name = WebUtility.HtmlEncode(vm.StoreName);
            sender.SendEmail(MailboxAddress.Parse(l.affiliate.Email),
                localizer["Your {0} affiliate panel sign-in link", vm.StoreName],
                $"<p>{localizer["Hello,"]}</p><p>{localizer["Use this link to sign in to your affiliate panel for {0}. It works once and expires in {1} minutes.", name, (int)PanelAuthService.MagicLinkLifetime.TotalMinutes]}</p>" +
                $"<p><a href=\"{WebUtility.HtmlEncode(url)}\">{localizer["Sign in to the affiliate panel"]}</a></p>" +
                $"<p>{localizer["If you did not ask for this, you can ignore this email."]}</p>");
            logger.LogInformation("MaxiAffiliates: panel sign-in link sent for affiliate {AffiliateId}", l.affiliate.Id);
        }
        // Same answer whether or not the address is registered (no account enumeration).
        return View("LoginSent", vm);
    }

    /// <summary>GET only shows a confirm button, so mail scanners that prefetch links cannot burn a single-use link.</summary>
    [HttpGet(Base + "/auth")]
    public async Task<IActionResult> Auth(string storeId, string? token)
    {
        var vm = await LoginModel(storeId);
        if (vm is null) return NotFound();
        Response.Headers["Referrer-Policy"] = "no-referrer";
        ViewData["Token"] = token;
        return View("AuthConfirm", vm);
    }

    [HttpPost(Base + "/auth")]
    [ValidateAntiForgeryToken]
    [RateLimitsFilter(ZoneLimits.Login, Scope = RateLimitsScope.RemoteAddress)]
    [ActionName("Auth")]
    public async Task<IActionResult> AuthPost(string storeId, string? token)
    {
        var session = await auth.Redeem(storeId, token, HttpContext.RequestAborted);
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (session is null)
        {
            var vm = await LoginModel(storeId);
            if (vm is null) return NotFound();
            vm.Error = localizer["This sign-in link is invalid or has expired. Request a new one."];
            return View("Login", vm);
        }
        Response.Cookies.Append(CookieName, session, new CookieOptions
        {
            HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Lax, Path = CookiePath(storeId),
            MaxAge = PanelAuthService.SessionLifetime, IsEssential = true
        });
        return RedirectToAction(nameof(Index), new { storeId });
    }

    [HttpGet(Base)]
    public async Task<IActionResult> Index(string storeId)
    {
        var aff = await Current(storeId);
        if (aff is null) return RedirectToAction(nameof(Login), new { storeId });
        var store = await stores.FindStore(storeId);
        if (store is null) return NotFound();
        var o = await panel.Overview(aff, ct: HttpContext.RequestAborted);
        Response.Headers["Cache-Control"] = "no-store";
        return View(new PanelViewModel
        {
            StoreId = storeId, StoreName = store.StoreName, Overview = o,
            ReferralLink = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/aff/{storeId}/{aff.Code}",
            PayoutMethod = aff.PayoutMethod, PayoutDestination = aff.PayoutDestination,
        });
    }

    [HttpPost(Base + "/payout-destination")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePayout(string storeId, PayoutMethodKind payoutMethod, string? payoutDestination)
    {
        var aff = await Current(storeId);
        if (aff is null) return RedirectToAction(nameof(Login), new { storeId });
        if (aff.Status == AffiliateStatus.Suspended)
        {
            TempData["MaxiAffPanelError"] = localizer["Your account is suspended; the payout address cannot be changed."].Value;
            return RedirectToAction(nameof(Index), new { storeId });
        }
        var old = aff.PayoutDestination;
        var err = await panel.UpdatePayoutDestination(aff, payoutMethod, payoutDestination, networks.BTC.NBitcoinNetwork, HttpContext.RequestAborted);
        if (err is not null)
        {
            TempData["MaxiAffPanelError"] = localizer[err].Value;
            return RedirectToAction(nameof(Index), new { storeId });
        }
        // A changed destination is the sensitive action: end every other session/link and tell the affiliate by email.
        await auth.RevokeAll(aff.Id, Request.Cookies[CookieName], HttpContext.RequestAborted);
        if (await emailSenders.IsComplete(storeId) && old != aff.PayoutDestination)
        {
            var store = await stores.FindStore(storeId);
            var sender = await emailSenders.GetEmailSender(storeId);
            sender.SendEmail(MailboxAddress.Parse(aff.Email), localizer["Your {0} affiliate payout address was changed", store?.StoreName ?? ""],
                $"<p>{localizer["Your payout address was changed to: {0}", WebUtility.HtmlEncode(aff.PayoutDestination)]}</p>" +
                $"<p>{localizer["If you did not make this change, contact the store right away."]}</p>");
        }
        TempData["MaxiAffPanelOk"] = localizer["Payout address saved. Future payouts go to the new address."].Value;
        return RedirectToAction(nameof(Index), new { storeId });
    }

    [HttpPost(Base + "/logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(string storeId)
    {
        await auth.Logout(storeId, Request.Cookies[CookieName], HttpContext.RequestAborted);
        Response.Cookies.Delete(CookieName, new CookieOptions { Path = CookiePath(storeId) });
        return RedirectToAction(nameof(Login), new { storeId });
    }

    Task<Affiliate?> Current(string storeId) => auth.Authenticate(storeId, Request.Cookies[CookieName], HttpContext.RequestAborted);

    async Task<PanelLoginViewModel?> LoginModel(string storeId)
    {
        var store = await stores.FindStore(storeId);
        if (store is null) return null;
        var settings = await affiliates.GetSettings(storeId, HttpContext.RequestAborted);
        if (!settings.Enabled) return null;
        return new PanelLoginViewModel { StoreId = storeId, StoreName = store.StoreName, EmailAvailable = await emailSenders.IsComplete(storeId) };
    }
}
