using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BTCPayServer.Plugins.MaxiAffiliates.Services;

/// <summary>
/// Registers every UI string of the plugin with BTCPay's translation system (Server Settings → Translations).
/// Source of truth: embedded Resources/Translations/en.json (English keys). M8 adds es.json.
/// </summary>
public class MaxiAffiliatesTranslationProvider : BTCPayServer.Services.IDefaultTranslationProvider
{
    public const string EnglishResource = "Resources/Translations/en.json";

    public static Dictionary<string, string> Load(string language = "en")
    {
        var asm = typeof(MaxiAffiliatesTranslationProvider).Assembly;
        var name = asm.GetManifestResourceNames().Single(n => n.EndsWith($"Resources.Translations.{language}.json"));
        using var s = asm.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(s)!;
    }

    public Task<KeyValuePair<string, string?>[]> GetDefaultTranslations() =>
        Task.FromResult(Load().Select(kv => KeyValuePair.Create(kv.Key, (string?)kv.Value)).ToArray());
}
