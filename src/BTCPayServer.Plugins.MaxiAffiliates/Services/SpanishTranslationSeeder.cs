#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services;
using BTCPayServer.Plugins.Translations;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.MaxiAffiliates.Services;

/// <summary>
/// BTCPay has one server-wide language dictionary (Server Settings → Translations). This service adds the plugin's
/// Spanish strings (embedded es.json) to every Spanish dictionary, creating a "Spanish" one (fallback English) if none exists.
/// Only missing sentences are inserted, so edits made by the admin in the BTCPay translation editor are kept.
/// It re-runs every 15 minutes because installing or updating BTCPay's Spanish language pack replaces the dictionary.
/// </summary>
public class SpanishTranslationSeeder(ApplicationDbContextFactory dbFactory, LocalizerService localizer, ILogger<SpanishTranslationSeeder> logger) : BackgroundService
{
    public const string DefaultDictionary = "Spanish";
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    public static bool IsSpanish(string dictId)
    {
        var d = dictId.Trim().ToLowerInvariant();
        return d is "es" || d.StartsWith("es-") || d.StartsWith("es_") || d.StartsWith("spanish") || d.StartsWith("español") || d.StartsWith("espanol");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Seed(stoppingToken); }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { logger.LogWarning(ex, "MaxiAffiliates: could not seed Spanish translations"); }
            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }

    public async Task<int> Seed(CancellationToken ct = default)
    {
        var es = MaxiAffiliatesTranslationProvider.Load("es");
        await using var ctx = dbFactory.CreateContext();
        var conn = ctx.Database.GetDbConnection();
        var dicts = (await conn.QueryAsync<string>("SELECT dict_id FROM lang_dictionaries")).Where(IsSpanish).ToList();
        if (dicts.Count == 0)
        {
            await conn.ExecuteAsync("INSERT INTO lang_dictionaries (dict_id, fallback, source) VALUES (@d, 'English', 'Custom') ON CONFLICT DO NOTHING", new { d = DefaultDictionary });
            dicts.Add(DefaultDictionary);
        }
        var keys = es.Keys.ToArray(); var values = es.Values.ToArray();
        var inserted = 0;
        foreach (var d in dicts)
            inserted += await conn.ExecuteAsync(
                "INSERT INTO lang_translations (dict_id, sentence, translation) SELECT @d, k, v FROM unnest(@keys, @values) AS t(k, v) ON CONFLICT (dict_id, sentence) DO NOTHING",
                new { d, keys, values });
        if (inserted > 0)
        {
            logger.LogInformation("MaxiAffiliates: added {Count} Spanish sentences to {Dicts}", inserted, string.Join(", ", dicts));
            await localizer.Load(); // refresh the in-memory dictionary if a Spanish one is active
        }
        return inserted;
    }
}
