using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using BTCPayServer.Plugins.MaxiAffiliates.Data;
using BTCPayServer.Plugins.MaxiAffiliates.Models;
using BTCPayServer.Plugins.MaxiAffiliates.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Plugins.MaxiAffiliates.Controllers;

[Area(Plugin.Area)]
[Authorize(Policy = MaxiAffiliatesPolicies.CanViewAffiliates, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UIAffiliateSettingsController(
    AffiliateService affiliates, ProgramSettingsService program, StoreRepository stores,
    CommissionLedger ledger, CommissionMaintenanceService maintenance,
    ApplicationDbContextFactory appDb, IStringLocalizer localizer) : Controller
{
    [HttpGet("~/stores/{storeId}/plugins/maxiaffiliates/settings")]
    public async Task<IActionResult> Settings(string storeId)
    {
        var ct = HttpContext.RequestAborted;
        var store = await stores.FindStore(storeId);
        if (store is null) return NotFound();
        await using var ctx = appDb.CreateContext();
        var plans = await ctx.Plans.AsNoTracking().Where(p => p.Offering.App.StoreDataId == storeId)
            .OrderBy(p => p.Name).Select(p => new PlanOption(p.Id, p.Name, p.Price, p.Currency)).ToArrayAsync(ct);
        var settings = await affiliates.GetSettings(storeId, ct);
        var storeDefault = store.GetStoreBlob().DefaultCurrency ?? "USD";
        return View(new ProgramSettingsViewModel
        {
            StoreId = storeId, Settings = settings, StoreDefaultCurrency = storeDefault,
            EffectiveBaseCurrency = CommissionEngine.BaseCurrency(settings, storeDefault),
            Rules = await program.ListRules(storeId, ct), Plans = plans
        });
    }

    [HttpPost("~/stores/{storeId}/plugins/maxiaffiliates/settings")]
    [Authorize(Policy = MaxiAffiliatesPolicies.CanManageAffiliates, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSettings(string storeId, [FromForm(Name = "Settings")] AffiliateStoreSettings settings)
    {
        settings.StoreId = storeId;
        var error = ProgramSettingsService.Validate(settings);
        if (error is null && !string.IsNullOrWhiteSpace(settings.LandingUrl) && !UIReferralController.TryGetLanding(settings.LandingUrl.Trim(), out _))
            error = "Enter a valid http(s) URL for the landing page.";
        if (error is not null) return Status(storeId, false, localizer[error]);
        await program.SaveSettings(settings, HttpContext.RequestAborted);
        return Status(storeId, true, localizer["Affiliate program settings saved."]);
    }

    [HttpPost("~/stores/{storeId}/plugins/maxiaffiliates/rules")]
    [Authorize(Policy = MaxiAffiliatesPolicies.CanManageAffiliates, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRule(string storeId, [FromForm(Name = "Rule")] CommissionRule rule)
    {
        rule.StoreId = storeId;
        var error = ProgramSettingsService.ValidateRule(rule);
        if (error is not null) return Status(storeId, false, localizer[error]);
        await program.UpsertRule(rule, HttpContext.RequestAborted);
        return Status(storeId, true, localizer["Commission rule saved."]);
    }

    [HttpPost("~/stores/{storeId}/plugins/maxiaffiliates/rules/{ruleId}/delete")]
    [Authorize(Policy = MaxiAffiliatesPolicies.CanManageAffiliates, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRule(string storeId, string ruleId)
    {
        if (!await program.DeleteRule(storeId, ruleId, HttpContext.RequestAborted)) return NotFound();
        return Status(storeId, true, localizer["Commission rule deleted."]);
    }

    [HttpGet("~/stores/{storeId}/plugins/maxiaffiliates/commissions")]
    public async Task<IActionResult> Commissions(string storeId, CommissionStatus? status)
    {
        var ct = HttpContext.RequestAborted;
        var settings = await affiliates.GetSettings(storeId, ct);
        return View(new CommissionsViewModel
        {
            StoreId = storeId, Status = status, MinPayoutSats = settings.MinPayoutSats,
            Rows = await program.ListCommissions(storeId, status, 200, ct),
            Balances = await ledger.Balances(storeId, ct),
            PendingCredits = await ledger.ListPendingCredits(storeId, ct),
            Adjustments = await ledger.AdjustmentTotals(storeId, ct)
        });
    }

    [HttpPost("~/stores/{storeId}/plugins/maxiaffiliates/commissions/run")]
    [Authorize(Policy = MaxiAffiliatesPolicies.CanManageAffiliates, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunMaintenance(string storeId)
    {
        await ledger.MakeRetriesDue(storeId, HttpContext.RequestAborted);
        var r = await maintenance.RunOnce(storeId, HttpContext.RequestAborted);
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Severity = StatusMessageModel.StatusSeverity.Success,
            Message = localizer["Retried {0} waiting payments ({1} credited); {2} commissions became payable.", r.Retried, r.Credited, r.Matured]
        });
        return RedirectToAction(nameof(Commissions), new { storeId });
    }

    [HttpPost("~/stores/{storeId}/plugins/maxiaffiliates/commissions/{commissionId}/reverse")]
    [Authorize(Policy = MaxiAffiliatesPolicies.CanManageAffiliates, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReverseCommission(string storeId, string commissionId)
    {
        var r = await ledger.ReverseCommission(storeId, commissionId, null, "Reversed manually", $"manual:{commissionId}", HttpContext.RequestAborted);
        if (r.Outcome == ReversalOutcome.NoCommission) return NotFound();
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Severity = r.Outcome is ReversalOutcome.Reversed or ReversalOutcome.ClawedBack ? StatusMessageModel.StatusSeverity.Success : StatusMessageModel.StatusSeverity.Info,
            Message = r.Outcome switch
            {
                ReversalOutcome.Reversed => localizer["Commission reversed."],
                ReversalOutcome.ClawedBack => localizer["Commission was already paid: {0} sats will be deducted from the next payout.", r.ClawedSats],
                _ => localizer["Nothing to reverse."]
            }
        });
        return RedirectToAction(nameof(Commissions), new { storeId });
    }

    IActionResult Status(string storeId, bool ok, string message)
    {
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Severity = ok ? StatusMessageModel.StatusSeverity.Success : StatusMessageModel.StatusSeverity.Error, Message = message
        });
        return RedirectToAction(nameof(Settings), new { storeId });
    }
}
