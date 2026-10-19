using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.MaxiAffiliates.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.MaxiAffiliates.Services;

/// <summary>What is known about a settled payment when it settles.</summary>
public record CallbackFacts(
    string InvoiceId, decimal Amount, string Currency, string? PlanId, string? SubscriberId,
    string? AffiliateCode, bool CommissionCredited, string CommissionOutcome, long Timestamp);

public record SubscriptionFacts(string? PlanId, string? OfferingId, string? SubscriberId, string? CustomerId);

/// <summary>Resolves offering / subscriber / customer ids (BTCPay subscriptions) for an invoice at delivery time.</summary>
public interface ISubscriptionFacts
{
    Task<SubscriptionFacts> Resolve(string storeId, string invoiceId, string? planId, string? subscriberId, CancellationToken ct);
}

/// <summary>Body of the signed POST. Field names are part of the public contract (docs/INTEGRATION.md).</summary>
public record CallbackPayload(
    string EventId, string Type, string StoreId, string? InvoiceId, string? PlanId, string? OfferingId, string? SubscriberId,
    string? CustomerId, string? Amount, string? Currency, string Status, string? AffiliateCode, bool CommissionCredited,
    string? CommissionOutcome, long Timestamp, bool Test);

public record CallbackSettingsView(bool Enabled, string? Url, bool HasSecret, DateTimeOffset? SecretCreatedAt);

/// <summary>
/// Seller-site order confirmation callback: on every settled invoice / subscription payment the store's callback URL gets
/// a POST signed with HMAC-SHA256 over "{timestamp}.{body}" (header MaxiAff-Signature: t=…,v1=…). Deliveries are stored,
/// retried with exponential backoff up to <see cref="MaxAttempts"/>, listed in the admin and can be redelivered.
/// The secret is kept encrypted (Data Protection), shown once, and never logged.
/// </summary>
public class CallbackService(
    IDbContextFactory<MaxiAffiliatesDbContext> dbFactory, TimeProvider clock, IDataProtectionProvider dataProtection,
    ICallbackTransport transport, ISubscriptionFacts subscriptions, CallbackUrlPolicy policy, ILogger<CallbackService> logger)
{
    public const string SignatureHeader = "MaxiAff-Signature";
    public const string EventIdHeader = "MaxiAff-Event-Id";
    public const string DeliveryIdHeader = "MaxiAff-Delivery-Id";
    public const int MaxAttempts = 8;
    public static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan MaxDelay = TimeSpan.FromHours(1);
    /// <summary>A claimed delivery is invisible to other workers this long (crash safety: it is retried afterwards).</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.Never };
    readonly IDataProtector _protector = dataProtection.CreateProtector("BTCPayServer.Plugins.MaxiAffiliates.CallbackSecret");
    readonly SemaphoreSlim _work = new(0);

    public CallbackUrlPolicy Policy => policy;

    /// <summary>Delay before attempt n+1 after n failed attempts: 10 s, 30 s, 90 s, 4.5 min, 13.5 min, 40 min, then 1 h (cap).</summary>
    public static TimeSpan RetryDelay(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(BaseDelay.TotalSeconds * Math.Pow(3, Math.Max(attempts - 1, 0)), MaxDelay.TotalSeconds));

    public static string EventIdFor(string invoiceId) => "evt_settled_" + invoiceId;

    public static string Sign(string secret, long timestamp, byte[] body)
    {
        var prefix = Encoding.UTF8.GetBytes(timestamp.ToString(CultureInfo.InvariantCulture) + ".");
        var msg = new byte[prefix.Length + body.Length];
        prefix.CopyTo(msg, 0); body.CopyTo(msg, prefix.Length);
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), msg)).ToLowerInvariant();
    }

    /// <summary>Reference verifier (what a receiver should do): constant-time compare and a ±5 min timestamp window.</summary>
    public static bool Verify(string secret, string? header, byte[] body, long nowUnix, int toleranceSeconds = 300)
    {
        if (string.IsNullOrEmpty(header)) return false;
        var parts = header.Split(',').Select(p => p.Trim().Split('=', 2)).Where(p => p.Length == 2).ToArray();
        if (!long.TryParse(parts.FirstOrDefault(p => p[0] == "t")?[1], NumberStyles.None, CultureInfo.InvariantCulture, out var t)) return false;
        if (Math.Abs(nowUnix - t) > toleranceSeconds) return false;
        var expected = Encoding.ASCII.GetBytes(Sign(secret, t, body));
        return parts.Where(p => p[0] == "v1").Any(p => CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(p[1])));
    }

    public async Task<CallbackSettingsView> GetSettings(string storeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var s = await db.CallbackSettings.AsNoTracking().FirstOrDefaultAsync(x => x.StoreId == storeId, ct);
        return s is null ? new(false, null, false, null) : new(s.Enabled, s.Url, s.SecretProtected is not null, s.SecretCreatedAt);
    }

    /// <summary>Returns an error message key, or null when saved.</summary>
    public async Task<string?> SaveSettings(string storeId, bool enabled, string? url, CancellationToken ct = default)
    {
        url = string.IsNullOrWhiteSpace(url) ? null : url.Trim();
        if (url is not null || enabled)
        {
            var err = policy.Validate(url, out _);
            if (err is not null) return err;
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var s = await db.CallbackSettings.FirstOrDefaultAsync(x => x.StoreId == storeId, ct);
        if (s is null) { s = new CallbackSettings { StoreId = storeId }; db.CallbackSettings.Add(s); }
        if (enabled && s.SecretProtected is null) return "Generate a signing secret before enabling the callback.";
        s.Enabled = enabled; s.Url = url; s.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Creates a new random secret (replacing the old one) and returns it. The caller shows it once.</summary>
    public async Task<string> RegenerateSecret(string storeId, CancellationToken ct = default)
    {
        var secret = "whsec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var s = await db.CallbackSettings.FirstOrDefaultAsync(x => x.StoreId == storeId, ct);
        if (s is null) { s = new CallbackSettings { StoreId = storeId }; db.CallbackSettings.Add(s); }
        s.SecretProtected = _protector.Protect(secret);
        s.SecretCreatedAt = s.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        logger.LogInformation("MaxiAffiliates: callback signing secret regenerated for store {Store}", storeId);
        return secret;
    }

    /// <summary>Queues the settled-payment event (no-op when the store has no enabled callback; idempotent per invoice).</summary>
    public async Task<bool> EnqueueSettled(string storeId, CallbackFacts facts, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var s = await db.CallbackSettings.AsNoTracking().FirstOrDefaultAsync(x => x.StoreId == storeId, ct);
        if (s is not { Enabled: true, Url: not null }) return false;
        var eventId = EventIdFor(facts.InvoiceId);
        if (await db.CallbackDeliveries.AnyAsync(d => d.StoreId == storeId && d.EventId == eventId && d.RedeliveryOfId == null, ct)) return false;
        var now = clock.GetUtcNow();
        // A short delay lets BTCPay's subscription service create the subscriber for a first plan payment before we read it.
        db.CallbackDeliveries.Add(new CallbackDelivery
        {
            StoreId = storeId, EventId = eventId, Kind = CallbackEventKind.InvoiceSettled, InvoiceId = facts.InvoiceId,
            Facts = JsonSerializer.Serialize(facts, Json), CreatedAt = now, NextAttemptAt = now + TimeSpan.FromSeconds(3)
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return false; } // concurrent duplicate settle event
        _work.Release();
        return true;
    }

    public async Task<(string? deliveryId, string? error)> EnqueueTest(string storeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var s = await db.CallbackSettings.AsNoTracking().FirstOrDefaultAsync(x => x.StoreId == storeId, ct);
        if (s?.Url is null) return (null, "Save a callback URL first.");
        if (s.SecretProtected is null) return (null, "Generate a signing secret before enabling the callback.");
        var now = clock.GetUtcNow();
        var d = new CallbackDelivery
        {
            StoreId = storeId, EventId = "evt_test_" + Ids.New()[..16], Kind = CallbackEventKind.Test,
            Facts = JsonSerializer.Serialize(new CallbackFacts("", 0m, "", null, null, null, false, "test", now.ToUnixTimeSeconds()), Json),
            CreatedAt = now, NextAttemptAt = now
        };
        db.CallbackDeliveries.Add(d);
        await db.SaveChangesAsync(ct);
        _work.Release();
        return (d.Id, null);
    }

    /// <summary>Manual redelivery: a new delivery with the same event id and the same body, attempts reset.</summary>
    public async Task<string?> Redeliver(string storeId, string deliveryId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var o = await db.CallbackDeliveries.AsNoTracking().FirstOrDefaultAsync(d => d.StoreId == storeId && d.Id == deliveryId, ct);
        if (o is null) return null;
        var now = clock.GetUtcNow();
        var d = new CallbackDelivery
        {
            StoreId = storeId, EventId = o.EventId, Kind = o.Kind, InvoiceId = o.InvoiceId, Facts = o.Facts, Payload = o.Payload,
            RedeliveryOfId = o.RedeliveryOfId ?? o.Id, CreatedAt = now, NextAttemptAt = now
        };
        db.CallbackDeliveries.Add(d);
        await db.SaveChangesAsync(ct);
        _work.Release();
        return d.Id;
    }

    public async Task<CallbackDelivery[]> ListDeliveries(string storeId, int take = 50, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.CallbackDeliveries.AsNoTracking().Where(d => d.StoreId == storeId)
            .OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id).Take(take).ToArrayAsync(ct);
    }

    /// <summary>Code of the affiliate the payment is attributed to (credited commission first, then the customer's binding), or null.</summary>
    public async Task<string?> AffiliateCodeFor(string storeId, string invoiceId, string customerKey, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Commissions.AsNoTracking().Where(c => c.StoreId == storeId && c.InvoiceId == invoiceId).Select(c => c.Affiliate.Code).FirstOrDefaultAsync(ct)
               ?? await db.Referrals.AsNoTracking().Where(r => r.StoreId == storeId && r.CustomerKey == customerKey).Select(r => r.Affiliate.Code).FirstOrDefaultAsync(ct);
    }

    public async Task WaitForWork(TimeSpan max, CancellationToken ct)
    {
        try { await _work.WaitAsync(max, ct); } catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
    }

    /// <summary>Sends every due delivery once. Safe to run concurrently: each delivery is claimed with a conditional update.</summary>
    public async Task<int> ProcessDue(CancellationToken ct = default, string? storeId = null)
    {
        var now = clock.GetUtcNow();
        string[] due;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var q = db.CallbackDeliveries.Where(d => d.Status == CallbackDeliveryStatus.Pending && d.NextAttemptAt <= now);
            if (storeId is not null) q = q.Where(d => d.StoreId == storeId);
            due = await q.OrderBy(d => d.NextAttemptAt).Select(d => d.Id).Take(25).ToArrayAsync(ct);
        }
        var n = 0;
        foreach (var id in due)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var claimed = await db.CallbackDeliveries
                .Where(d => d.Id == id && d.Status == CallbackDeliveryStatus.Pending && d.NextAttemptAt <= now)
                .ExecuteUpdateAsync(u => u.SetProperty(d => d.NextAttemptAt, now + Lease), ct);
            if (claimed != 1) continue;
            var d = await db.CallbackDeliveries.FirstAsync(x => x.Id == id, ct);
            await Attempt(db, d, ct);
            n++;
        }
        return n;
    }

    async Task Attempt(MaxiAffiliatesDbContext db, CallbackDelivery d, CancellationToken ct)
    {
        var s = await db.CallbackSettings.AsNoTracking().FirstOrDefaultAsync(x => x.StoreId == d.StoreId, ct);
        var now = clock.GetUtcNow();
        d.LastAttemptAt = now;
        if (s?.Url is null || s.SecretProtected is null || (d.Kind == CallbackEventKind.InvoiceSettled && !s.Enabled))
        {
            d.Status = CallbackDeliveryStatus.Failed; d.LastError = "Callback disabled or not configured"; d.LastHttpStatus = null;
            await db.SaveChangesAsync(ct);
            return;
        }
        if (policy.Validate(s.Url, out var uri) is { } urlError)
        {
            d.Status = CallbackDeliveryStatus.Failed; d.LastError = urlError;
            await db.SaveChangesAsync(ct);
            return;
        }
        d.Payload ??= await BuildPayload(d, ct);
        var body = Encoding.UTF8.GetBytes(d.Payload);
        var t = now.ToUnixTimeSeconds();
        string secret;
        try { secret = _protector.Unprotect(s.SecretProtected); }
        catch (CryptographicException)
        {
            d.Status = CallbackDeliveryStatus.Failed; d.LastError = "Signing secret unreadable: regenerate it";
            await db.SaveChangesAsync(ct);
            return;
        }
        var headers = new Dictionary<string, string>
        {
            [SignatureHeader] = $"t={t},v1={Sign(secret, t, body)}",
            [EventIdHeader] = d.EventId,
            [DeliveryIdHeader] = d.Id,
            ["User-Agent"] = "BTCPayServer-MaxiAffiliates/1.0"
        };
        d.Attempts++;
        var r = await transport.Post(uri!, body, headers, ct);
        d.LastHttpStatus = r.Status;
        d.LastError = r.Error is null ? null : r.Error.Length > 500 ? r.Error[..500] : r.Error;
        if (r.Status is >= 200 and < 300)
            d.Status = CallbackDeliveryStatus.Succeeded;
        else if (ShouldRetry(r.Status) && d.Attempts < MaxAttempts)
            d.NextAttemptAt = now + RetryDelay(d.Attempts);
        else
            d.Status = CallbackDeliveryStatus.Failed;
        await db.SaveChangesAsync(ct);
        // Host only: the URL path/query may carry the seller's own token.
        logger.LogInformation("MaxiAffiliates: callback {Delivery} ({Event}) to {Host} attempt {Attempt} -> {Status}",
            d.Id, d.EventId, uri!.Host, d.Attempts, r.Status?.ToString(CultureInfo.InvariantCulture) ?? r.Error);
    }

    /// <summary>Retry on network errors, timeouts, 408, 425, 429 and 5xx. Other 4xx are final (the receiver rejected the event).</summary>
    public static bool ShouldRetry(int? status) => status is null or 408 or 425 or 429 or >= 500;

    async Task<string> BuildPayload(CallbackDelivery d, CancellationToken ct)
    {
        var f = JsonSerializer.Deserialize<CallbackFacts>(d.Facts, Json)!;
        CallbackPayload p;
        if (d.Kind == CallbackEventKind.Test)
            p = new(d.EventId, "test", d.StoreId, null, null, null, null, null, null, null, "test", null, false, null, f.Timestamp, true);
        else
        {
            var sub = await subscriptions.Resolve(d.StoreId, f.InvoiceId, f.PlanId, f.SubscriberId, ct);
            p = new(d.EventId, "invoice.settled", d.StoreId, f.InvoiceId, sub.PlanId ?? f.PlanId, sub.OfferingId, sub.SubscriberId ?? f.SubscriberId,
                sub.CustomerId, f.Amount.ToString(CultureInfo.InvariantCulture), f.Currency, "settled", f.AffiliateCode,
                f.CommissionCredited, f.CommissionOutcome, f.Timestamp, false);
        }
        return JsonSerializer.Serialize(p, Json);
    }
}
