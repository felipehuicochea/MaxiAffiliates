using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Plugins.MaxiAffiliates.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.MaxiAffiliates;

[Area(Plugin.Area)]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanViewProfile)]
public class MaxiAffiliatesController(MaxiAffiliatesDbContext db) : Controller
{
    [HttpGet("~/plugins/maxiaffiliates")]
    public async Task<IActionResult> Index()
    {
        ViewData["AffiliateCount"] = await db.Affiliates.CountAsync();
        return View();
    }

    /// <summary>Anonymous dev/ops probe: plugin loaded + schema migrated. Exposes no store data.</summary>
    [AllowAnonymous]
    [HttpGet("~/plugins/maxiaffiliates/health")]
    public async Task<IActionResult> Health()
    {
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();
        return Json(new
        {
            plugin = "BTCPayServer.Plugins.MaxiAffiliates",
            version = typeof(Plugin).Assembly.GetName().Version?.ToString(),
            schema = MaxiAffiliatesDbContext.Schema,
            appliedMigrations = applied,
            pendingMigrations = pending
        });
    }
}
