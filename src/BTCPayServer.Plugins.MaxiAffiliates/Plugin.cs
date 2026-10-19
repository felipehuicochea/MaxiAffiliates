using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Plugins.MaxiAffiliates.Attribution;
using BTCPayServer.Plugins.MaxiAffiliates.Data;
using BTCPayServer.Plugins.MaxiAffiliates.Services;
using BTCPayServer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BTCPayServer.Plugins.MaxiAffiliates;

public class Plugin : BaseBTCPayServerPlugin
{
    public const string Area = "MaxiAffiliates";
    public const string ViewsDirectory = "/Plugins/" + Area + "/Views";

    public override string Identifier => "BTCPayServer.Plugins.MaxiAffiliates";
    public override string Name => "MaxiAffiliates";
    public override string Description => "Affiliate program: referral links, commissions on invoices and subscription renewals, payouts via pull payments.";

    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    {
        new IBTCPayServerPlugin.PluginDependency { Identifier = nameof(BTCPayServer), Condition = ">=2.4.5" }
    };

    public override void Execute(IServiceCollection services)
    {
        services.AddPluginDbContext<MaxiAffiliatesDbContext>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IAttributionStrategy, FirstClickAttributionStrategy>();
        services.AddSingleton<AffiliateService>();
        services.AddSingleton<ReferralService>();
        services.AddSingleton<PlanCheckoutService>();
        services.AddSingleton<CommissionEngine>();
        services.AddSingleton<ProgramSettingsService>();
        services.AddSingleton<CommissionLedger>();
        services.AddSingleton<InvoiceCommissionMapper>();
        services.AddHostedService<InvoiceSettledListener>();
        services.AddSingleton<CommissionMaintenanceService>();
        services.AddHostedService(sp => sp.GetRequiredService<CommissionMaintenanceService>());
        services.AddTranslationProvider<MaxiAffiliatesTranslationProvider>();
        services.AddUIExtension("store-integrations-nav", $"{ViewsDirectory}/StoreNavExtension.cshtml");
        services.AddPolicyDefinitions(
            new PolicyDefinition(MaxiAffiliatesPolicies.CanViewAffiliates,
                new PermissionDisplay("View your affiliates", "Allows viewing affiliates on all your stores."),
                new PermissionDisplay("View your affiliates", "Allows viewing affiliates on the selected stores."),
                includedByPermissions: [Policies.CanViewStoreSettings]),
            new PolicyDefinition(MaxiAffiliatesPolicies.CanManageAffiliates,
                new PermissionDisplay("Manage your affiliates", "Allows approving, suspending and banning affiliates on all your stores."),
                new PermissionDisplay("Manage your affiliates", "Allows approving, suspending and banning affiliates on the selected stores."),
                new[] { MaxiAffiliatesPolicies.CanViewAffiliates },
                includedByPermissions: [Policies.CanModifyStoreSettings]));
        base.Execute(services);
    }
}
