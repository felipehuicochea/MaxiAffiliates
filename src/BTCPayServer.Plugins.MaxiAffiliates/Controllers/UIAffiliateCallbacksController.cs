using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.MaxiAffiliates.Models;
using BTCPayServer.Plugins.MaxiAffiliates.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Plugins.MaxiAffiliates.Controllers;

/// <summary>Seller-site order confirmation callback: URL, signing secret, test send and delivery log with redelivery.</summary>
[Area(Plugin.Area)]
[Authorize(Policy = MaxiAffiliatesPolicies.CanViewAffiliates, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UIAffiliateCallbacksController(CallbackService callbacks, IStringLocalizer localizer) : Controller
{
    [HttpGet("~/stores/{storeId}/plugins/maxiaffiliates/callbacks")]
    public async Task<IActionResult> Callbacks(string storeId) => View(await Model(storeId, null));

    async Task<CallbacksViewModel> Model(string storeId, string? newSecret) => new()
    {
        StoreId = storeId, NewSecret = newSecret, HttpsOnly = !callbacks.Policy.AllowPrivateHosts,
        Settings = await callbacks.GetSettings(storeId, HttpContext.RequestAborted),
        Deliveries = await callbacks.ListDeliveries(storeId, 50, HttpContext.RequestAborted)
    };

    [HttpPost("~/stores/{storeId}/plugins/maxiaffiliates/callbacks")]
    [Authorize(Policy = MaxiAffiliatesPolicies.CanManageAffiliates, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCallback(string storeId, bool enabled, string? url)
    {
        var error = await callbacks.SaveSettings(storeId, enabled, url, HttpContext.RequestAborted);
        return Done(storeId, error is null, localizer[error ?? "Callback settings saved."]);
    }

    /// <summary>Renders the page directly (no redirect) so the new secret is never put in TempData or a URL.</summary>
    [HttpPost("~/stores/{storeId}/plugins/maxiaffiliates/callbacks/secret")]
    [Authorize(Policy = MaxiAffiliatesPolicies.CanManageAffiliates, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegenerateSecret(string storeId)
    {
        var secret = await callbacks.RegenerateSecret(storeId, HttpContext.RequestAborted);
        Response.Headers.CacheControl = "no-store";
        return View(nameof(Callbacks), await Model(storeId, secret));
    }

    [HttpPost("~/stores/{storeId}/plugins/maxiaffiliates/callbacks/test")]
    [Authorize(Policy = MaxiAffiliatesPolicies.CanManageAffiliates, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendTest(string storeId)
    {
        var (id, error) = await callbacks.EnqueueTest(storeId, HttpContext.RequestAborted);
        if (id is not null) await callbacks.ProcessDue(HttpContext.RequestAborted, storeId);
        return Done(storeId, error is null, localizer[error ?? "Test event sent. See the delivery log below."]);
    }

    [HttpPost("~/stores/{storeId}/plugins/maxiaffiliates/callbacks/{deliveryId}/redeliver")]
    [Authorize(Policy = MaxiAffiliatesPolicies.CanManageAffiliates, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Redeliver(string storeId, string deliveryId)
    {
        if (await callbacks.Redeliver(storeId, deliveryId, HttpContext.RequestAborted) is null) return NotFound();
        await callbacks.ProcessDue(HttpContext.RequestAborted, storeId);
        return Done(storeId, true, localizer["Event redelivered. See the delivery log below."]);
    }

    IActionResult Done(string storeId, bool ok, string message)
    {
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Severity = ok ? StatusMessageModel.StatusSeverity.Success : StatusMessageModel.StatusSeverity.Error, Message = message
        });
        return RedirectToAction(nameof(Callbacks), new { storeId });
    }
}
