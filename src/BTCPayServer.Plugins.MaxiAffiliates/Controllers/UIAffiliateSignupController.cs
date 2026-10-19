using System.Threading.Tasks;
using BTCPayServer.Plugins.MaxiAffiliates.Models;
using BTCPayServer.Plugins.MaxiAffiliates.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Plugins.MaxiAffiliates.Controllers;

/// <summary>Public (anonymous) affiliate signup for a store.</summary>
[Area(Plugin.Area)]
[AllowAnonymous]
public class UIAffiliateSignupController(
    AffiliateService affiliates, StoreRepository stores, BTCPayNetworkProvider networks, IStringLocalizer localizer) : Controller
{
    [HttpGet("~/plugins/maxiaffiliates/{storeId}/signup")]
    public async Task<IActionResult> Signup(string storeId)
    {
        var vm = await BaseModel(storeId);
        return vm is null ? NotFound() : View(vm);
    }

    [HttpPost("~/plugins/maxiaffiliates/{storeId}/signup")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Signup(string storeId, SignupViewModel input)
    {
        var vm = await BaseModel(storeId);
        if (vm is null) return NotFound();
        vm.Email = input.Email; vm.Name = input.Name; vm.PayoutMethod = input.PayoutMethod; vm.PayoutDestination = input.PayoutDestination;
        var result = await affiliates.Signup(
            new SignupRequest(storeId, input.Email ?? "", input.Name, input.PayoutMethod, input.PayoutDestination ?? ""),
            networks.BTC.NBitcoinNetwork, HttpContext.RequestAborted);
        if (!result.Success)
        {
            ModelState.AddModelError(result.ErrorField ?? string.Empty, localizer[result.ErrorKey!]);
            return View(vm);
        }
        return View("SignupDone", new SignupDoneViewModel { StoreName = vm.StoreName, Status = result.Affiliate!.Status, Code = result.Affiliate.Code });
    }

    async Task<SignupViewModel?> BaseModel(string storeId)
    {
        var store = await stores.FindStore(storeId);
        if (store is null) return null;
        var settings = await affiliates.GetSettings(storeId);
        if (!settings.Enabled) return null;
        return new SignupViewModel
        {
            StoreId = storeId, StoreName = store.StoreName,
            LightningEnabled = settings.LightningEnabled, OnChainEnabled = settings.OnChainEnabled,
            PayoutMethod = settings.LightningEnabled ? Data.PayoutMethodKind.Lightning : Data.PayoutMethodKind.OnChain
        };
    }
}
