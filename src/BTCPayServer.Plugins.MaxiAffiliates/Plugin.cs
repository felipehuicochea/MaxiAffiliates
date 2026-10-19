using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.MaxiAffiliates.Attribution;
using BTCPayServer.Plugins.MaxiAffiliates.Data;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.MaxiAffiliates;

public class Plugin : BaseBTCPayServerPlugin
{
    public const string Area = "MaxiAffiliates";
    public const string ViewsDirectory = "/Plugins/" + Area + "/Views";

    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    {
        new IBTCPayServerPlugin.PluginDependency { Identifier = nameof(BTCPayServer), Condition = ">=2.4.5" }
    };

    public override void Execute(IServiceCollection services)
    {
        services.AddPluginDbContext<MaxiAffiliatesDbContext>();
        services.AddSingleton<IAttributionStrategy, FirstClickAttributionStrategy>();
        services.AddUIExtension("user-nav", $"{ViewsDirectory}/NavExtension.cshtml");
    }
}
