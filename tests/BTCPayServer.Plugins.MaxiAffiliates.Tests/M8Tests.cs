using System.Text.RegularExpressions;
using BTCPayServer.Plugins.MaxiAffiliates.Services;

namespace BTCPayServer.Plugins.MaxiAffiliates.Tests;

public class SpanishTranslationTests
{
    static readonly Dictionary<string, string> En = MaxiAffiliatesTranslationProvider.Load("en");
    static readonly Dictionary<string, string> Es = MaxiAffiliatesTranslationProvider.Load("es");
    // Same in both languages on purpose: brand/protocol words, units and CSS classes.
    static readonly HashSet<string> SameOnPurpose = ["Lightning", "On-chain", "Plan", "Sats", "manual", "bg-success", "bg-warning text-dark", "bg-danger", "bg-primary"];

    [Fact]
    public void EsHasExactlyTheSameKeysAsEn()
    {
        Assert.Empty(En.Keys.Except(Es.Keys).OrderBy(k => k));
        Assert.Empty(Es.Keys.Except(En.Keys).OrderBy(k => k));
    }

    [Fact]
    public void EveryStringIsTranslated()
    {
        Assert.Empty(Es.Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key));
        Assert.Empty(Es.Where(kv => kv.Value == kv.Key && !SameOnPurpose.Contains(kv.Key)).Select(kv => kv.Key));
    }

    [Fact]
    public void PlaceholdersArePreserved()
    {
        static string[] Ph(string s) => Regex.Matches(s, @"\{\d+\}").Select(m => m.Value).Order().ToArray();
        Assert.Empty(Es.Where(kv => !Ph(kv.Key).SequenceEqual(Ph(kv.Value))).Select(kv => kv.Key));
    }

    [Theory]
    [InlineData("Spanish", true)] [InlineData("Español", true)] [InlineData("es-419", true)] [InlineData("es", true)]
    [InlineData("English", false)] [InlineData("Estonian", false)] [InlineData("Portuguese", false)]
    public void DetectsSpanishDictionaries(string name, bool spanish) => Assert.Equal(spanish, SpanishTranslationSeeder.IsSpanish(name));
}
