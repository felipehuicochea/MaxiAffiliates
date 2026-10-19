using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.MaxiAffiliates.Data;
using BTCPayServer.Plugins.MaxiAffiliates.Models;
using BTCPayServer.Plugins.MaxiAffiliates.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Plugins.MaxiAffiliates.Controllers;

[Area(Plugin.Area)]
[Authorize(Policy = MaxiAffiliatesPolicies.CanViewAffiliates, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UIAffiliatesAdminController(AffiliateService affiliates, PanelAuthService panelAuth, BTCPayServer.Plugins.Emails.Services.EmailSenderFactory emailSenders, IStringLocalizer localizer) : Controller
{
    [HttpGet("~/stores/{storeId}/plugins/maxiaffiliates/affiliates")]
    public async Task<IActionResult> Index(string storeId, AffiliateStatus? status, string? search)
    {
        var vm = new AffiliatesListViewModel
        {
            StoreId = storeId, Status = status, Search = search,
            SignupUrl = Url.Action(nameof(UIAffiliateSignupController.Signup), "UIAffiliateSignup", new { storeId, area = Plugin.Area }, Request.Scheme) ?? "",
            Affiliates = await affiliates.List(storeId, status, search, HttpContext.RequestAborted),
            LandingUrl = (await affiliates.GetSettings(storeId, HttpContext.RequestAborted)).LandingUrl,
            ReferralLinkPrefix = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/aff/{storeId}/",
            PanelLoginUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/plugins/maxiaffiliates/{storeId}/panel/login",
            EmailConfigured = await emailSenders.IsComplete(storeId),
            ScriptUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/plugins/maxiaffiliates/js/maxiaff.js"
        };
        return View(vm);
    }

    [HttpPost("~/stores/{storeId}/plugins/maxiaffiliates/referral-settings")]
    [Authorize(Policy = MaxiAffiliatesPolicies.CanManageAffiliates, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveReferralSettings(string storeId, string? landingUrl)
    {
        if (!string.IsNullOrWhiteSpace(landingUrl) && !UIReferralController.TryGetLanding(landingUrl.Trim(), out _))
        {
            TempData.SetStatusMessageModel(new StatusMessageModel { Severity = StatusMessageModel.StatusSeverity.Error, Message = localizer["Enter a valid http(s) URL for the landing page."] });
            return RedirectToAction(nameof(Index), new { storeId });
        }
        await affiliates.SaveLandingUrl(storeId, landingUrl, HttpContext.RequestAborted);
        TempData.SetStatusMessageModel(new StatusMessageModel { Severity = StatusMessageModel.StatusSeverity.Success, Message = localizer["Referral settings saved."] });
        return RedirectToAction(nameof(Index), new { storeId });
    }

    [HttpPost("~/stores/{storeId}/plugins/maxiaffiliates/affiliates/{affiliateId}/{op}")]
    [Authorize(Policy = MaxiAffiliatesPolicies.CanManageAffiliates, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Act(string storeId, string affiliateId, AffiliateAction op)
    {
        var (ok, aff) = await affiliates.Apply(storeId, affiliateId, op, HttpContext.RequestAborted);
        if (aff is null) return NotFound();
        if (ok && aff.Status == AffiliateStatus.Banned)
            await panelAuth.RevokeAll(aff.Id, ct: HttpContext.RequestAborted);
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Severity = ok ? StatusMessageModel.StatusSeverity.Success : StatusMessageModel.StatusSeverity.Error,
            Message = ok ? localizer["Affiliate {0} is now {1}.", aff.Email, localizer[aff.Status.ToString()]]
                         : localizer["This action is not allowed for an affiliate in status {0}.", localizer[aff.Status.ToString()]]
        });
        return RedirectToAction(nameof(Index), new { storeId });
    }

    /// <summary>Fallback when the store cannot send email: a 30-day panel link the admin hands over. Shown once; reissuing revokes the previous one.</summary>
    [HttpPost("~/stores/{storeId}/plugins/maxiaffiliates/affiliates/{affiliateId}/panel-link")]
    [Authorize(Policy = MaxiAffiliatesPolicies.CanManageAffiliates, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PanelLink(string storeId, string affiliateId)
    {
        var token = await panelAuth.IssueAdminLink(storeId, affiliateId, HttpContext.RequestAborted);
        if (token is null)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel { Severity = StatusMessageModel.StatusSeverity.Error, Message = localizer["A panel link cannot be issued for this affiliate."] });
            return RedirectToAction(nameof(Index), new { storeId });
        }
        TempData["MaxiAffPanelLink"] = Url.Action("Auth", "UIAffiliatePanel", new { storeId, token, area = Plugin.Area }, Request.Scheme);
        TempData["MaxiAffPanelLinkFor"] = affiliateId;
        return RedirectToAction(nameof(Index), new { storeId });
    }
}
