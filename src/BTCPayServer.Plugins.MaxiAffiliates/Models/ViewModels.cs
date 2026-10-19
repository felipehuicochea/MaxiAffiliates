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
