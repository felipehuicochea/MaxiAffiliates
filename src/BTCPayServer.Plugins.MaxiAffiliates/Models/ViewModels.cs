using BTCPayServer.Plugins.MaxiAffiliates.Data;

namespace BTCPayServer.Plugins.MaxiAffiliates.Models;

public class SignupViewModel
{
    public string StoreId { get; set; } = "";
    public string StoreName { get; set; } = "";
    public bool LightningEnabled { get; set; }
    public bool OnChainEnabled { get; set; }
    public string? Email { get; set; }
    public string? Name { get; set; }
    public PayoutMethodKind PayoutMethod { get; set; } = PayoutMethodKind.Lightning;
    public string? PayoutDestination { get; set; }
}

public class SignupDoneViewModel
{
    public string StoreName { get; set; } = "";
    public AffiliateStatus Status { get; set; }
    public string Code { get; set; } = "";
}

public class AffiliatesListViewModel
{
    public string StoreId { get; set; } = "";
    public AffiliateStatus? Status { get; set; }
    public string? Search { get; set; }
    public string SignupUrl { get; set; } = "";
    public string? LandingUrl { get; set; }
    public string ReferralLinkPrefix { get; set; } = "";
    public string ScriptUrl { get; set; } = "";
    public Affiliate[] Affiliates { get; set; } = [];
}

public record PlanOption(string Id, string Name, decimal Price, string Currency);

public class ProgramSettingsViewModel
{
    public string StoreId { get; set; } = null!;
    public BTCPayServer.Plugins.MaxiAffiliates.Data.AffiliateStoreSettings Settings { get; set; } = null!;
    public string StoreDefaultCurrency { get; set; } = null!;
    public string EffectiveBaseCurrency { get; set; } = null!;
    public BTCPayServer.Plugins.MaxiAffiliates.Data.CommissionRule[] Rules { get; set; } = [];
    public PlanOption[] Plans { get; set; } = [];
    public BTCPayServer.Plugins.MaxiAffiliates.Data.CommissionRule Rule { get; set; } = new() { Value = 20 };
}

public class CommissionsViewModel
{
    public string StoreId { get; set; } = null!;
    public BTCPayServer.Plugins.MaxiAffiliates.Data.CommissionStatus? Status { get; set; }
    public BTCPayServer.Plugins.MaxiAffiliates.Services.CommissionRow[] Rows { get; set; } = [];
    public BTCPayServer.Plugins.MaxiAffiliates.Services.AffiliateBalance[] Balances { get; set; } = [];
    public PendingCredit[] PendingCredits { get; set; } = [];
    public System.Collections.Generic.Dictionary<string, long> Adjustments { get; set; } = new();
    public long MinPayoutSats { get; set; }
}

public class PayoutsViewModel
{
    public string StoreId { get; set; } = null!;
    public AffiliateStoreSettings Settings { get; set; } = null!;
    public System.DateTimeOffset NextCycle { get; set; }
    public PayoutCycle[] Cycles { get; set; } = [];
    public BTCPayServer.Plugins.MaxiAffiliates.Services.AffiliateBalance[] Balances { get; set; } = [];
}

public class PayoutCycleViewModel
{
    public string StoreId { get; set; } = null!;
    public PayoutCycle Cycle { get; set; } = null!;
    public BTCPayServer.Plugins.MaxiAffiliates.Services.PayoutCycleService.BatchRow[] Rows { get; set; } = [];
}
