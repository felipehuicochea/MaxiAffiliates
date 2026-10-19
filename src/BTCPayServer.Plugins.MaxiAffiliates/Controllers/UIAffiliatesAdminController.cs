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
public class UIAffiliatesAdminController(AffiliateService affiliates, IStringLocalizer localizer) : Controller
{
    [HttpGet("~/stores/{storeId}/plugins/maxiaffiliates/affiliates")]
    public async Task<IActionResult> Index(string storeId, AffiliateStatus? status, string? search)
    {
        var vm = new AffiliatesListViewModel
        {
            StoreId = storeId, Status = status, Search = search,
            SignupUrl = Url.Action(nameof(UIAffiliateSignupController.Signup), "UIAffiliateSignup", new { storeId, area = Plugin.Area }, Request.Scheme) ?? "",
            Affiliates = await affiliates.List(storeId, status, search, HttpContext.RequestAborted)
        };
        return View(vm);
    }

    [HttpPost("~/stores/{storeId}/plugins/maxiaffiliates/affiliates/{affiliateId}/{op}")]
    [Authorize(Policy = MaxiAffiliatesPolicies.CanManageAffiliates, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Act(string storeId, string affiliateId, AffiliateAction op)
    {
        var (ok, aff) = await affiliates.Apply(storeId, affiliateId, op, HttpContext.RequestAborted);
        if (aff is null) return NotFound();
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Severity = ok ? StatusMessageModel.StatusSeverity.Success : StatusMessageModel.StatusSeverity.Error,
            Message = ok ? localizer["Affiliate {0} is now {1}.", aff.Email, localizer[aff.Status.ToString()]]
                         : localizer["This action is not allowed for an affiliate in status {0}.", localizer[aff.Status.ToString()]]
        });
        return RedirectToAction(nameof(Index), new { storeId });
    }
}
