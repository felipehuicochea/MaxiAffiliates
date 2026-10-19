using System;
using System.Linq;
using System.Text;
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
public class UIAffiliatePayoutsController(PayoutCycleService cycles, CommissionLedger ledger, AffiliateService affiliates,
    TimeProvider clock, IStringLocalizer localizer) : Controller
{
    [HttpGet("~/stores/{storeId}/plugins/maxiaffiliates/payouts")]
    public async Task<IActionResult> Index(string storeId)
    {
        var ct = HttpContext.RequestAborted;
        var s = await affiliates.GetSettings(storeId, ct);
        var (_, boundary) = PayoutCycleService.CurrentCycle(s.PayoutFrequency, s.PayoutDay, clock.GetUtcNow());
        var next = s.PayoutFrequency == PayoutFrequency.Weekly ? boundary.AddDays(7) : boundary.AddMonths(1);
        return View(new PayoutsViewModel
        {
            StoreId = storeId, Settings = s, NextCycle = next, Cycles = await cycles.ListCycles(storeId, 50, ct),
            Balances = await ledger.Balances(storeId, ct)
        });
    }

    [HttpGet("~/stores/{storeId}/plugins/maxiaffiliates/payouts/{cycleId}")]
    public async Task<IActionResult> Cycle(string storeId, string cycleId)
    {
        var (c, rows) = await cycles.GetCycle(storeId, cycleId, HttpContext.RequestAborted);
        if (c is null) return NotFound();
        return View(new PayoutCycleViewModel { StoreId = storeId, Cycle = c, Rows = rows });
    }

    [HttpGet("~/stores/{storeId}/plugins/maxiaffiliates/payouts/{cycleId}/csv")]
    public async Task<IActionResult> Csv(string storeId, string cycleId)
    {
        var (c, rows) = await cycles.GetCycle(storeId, cycleId, HttpContext.RequestAborted);
        if (c is null) return NotFound();
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(PayoutCycleService.ToCsv(c, rows))).ToArray(),
            "text/csv", $"commissions-due-{c.CycleKey}.csv");
    }

    [HttpPost("~/stores/{storeId}/plugins/maxiaffiliates/payouts/run")]
    [Authorize(Policy = MaxiAffiliatesPolicies.CanManageAffiliates, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Run(string storeId)
    {
        var r = await cycles.RunCycle(storeId, manual: true, HttpContext.RequestAborted);
        if (r.Cycle is null)
        {
            Message(false, localizer["The affiliate program is disabled."]);
            return RedirectToAction(nameof(Index), new { storeId });
        }
        Message(true, localizer["Payout cycle created: {0} payouts due, {1} sats.", r.Cycle.BatchCount, r.Cycle.TotalSats]);
        return RedirectToAction(nameof(Cycle), new { storeId, cycleId = r.Cycle.Id });
    }

    [HttpPost("~/stores/{storeId}/plugins/maxiaffiliates/payouts/{cycleId}/approve-all")]
    [Authorize(Policy = MaxiAffiliatesPolicies.CanManageAffiliates, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveAll(string storeId, string cycleId)
    {
        var (c, rows) = await cycles.GetCycle(storeId, cycleId, HttpContext.RequestAborted);
        if (c is null) return NotFound();
        int ok = 0, failed = 0;
        foreach (var r in rows.Where(r => r.Batch.State == PayoutBatchState.Draft))
            if ((await cycles.Approve(storeId, r.Batch.Id, HttpContext.RequestAborted)).Outcome == ApproveOutcome.Approved) ok++; else failed++;
        Message(failed == 0, localizer["{0} payouts created, {1} failed.", ok, failed]);
        return RedirectToAction(nameof(Cycle), new { storeId, cycleId });
    }

    [HttpPost("~/stores/{storeId}/plugins/maxiaffiliates/batches/{batchId}/{op}")]
    [Authorize(Policy = MaxiAffiliatesPolicies.CanManageAffiliates, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Batch(string storeId, string batchId, string op, string cycleId)
    {
        if (op == "approve")
        {
            var r = await cycles.Approve(storeId, batchId, HttpContext.RequestAborted);
            if (r.Outcome == ApproveOutcome.NotFound) return NotFound();
            Message(r.Outcome == ApproveOutcome.Approved, r.Outcome switch
            {
                ApproveOutcome.Approved => localizer["Payout created in BTCPay."],
                ApproveOutcome.Failed => localizer["Payout failed: {0}", r.Error ?? ""],
                _ => localizer["This payout was already processed."]
            });
        }
        else if (op == "cancel")
            Message(await cycles.Cancel(storeId, batchId, HttpContext.RequestAborted),
                localizer["Payout cancelled; the amount returns to the affiliate's balance."]);
        else return NotFound();
        return RedirectToAction(nameof(Cycle), new { storeId, cycleId });
    }

    void Message(bool ok, string text) => TempData.SetStatusMessageModel(new StatusMessageModel
    {
        Severity = ok ? StatusMessageModel.StatusSeverity.Success : StatusMessageModel.StatusSeverity.Error, Message = text
    });
}
