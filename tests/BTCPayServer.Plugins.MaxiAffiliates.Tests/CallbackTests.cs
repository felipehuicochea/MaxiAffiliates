using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BTCPayServer.Plugins.MaxiAffiliates.Data;
using BTCPayServer.Plugins.MaxiAffiliates.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BTCPayServer.Plugins.MaxiAffiliates.Tests;

public class CallbackSignatureTests
{
    static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"eventId\":\"evt_1\"}");
    const string Secret = "whsec_test";
    static string Header(long t, string secret = Secret, byte[]? body = null) => $"t={t},v1={CallbackService.Sign(secret, t, body ?? Body)}";

    [Fact] public void SignatureIsHmacSha256OverTimestampDotBody()
    {
        var expected = Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes("1700000000." + Encoding.UTF8.GetString(Body)))).ToLowerInvariant();
        Assert.Equal(expected, CallbackService.Sign(Secret, 1700000000, Body));
    }
    [Fact] public void ValidSignatureVerifies() => Assert.True(CallbackService.Verify(Secret, Header(1000), Body, 1000));
    [Fact] public void TamperedBodyFails() => Assert.False(CallbackService.Verify(Secret, Header(1000), Encoding.UTF8.GetBytes("{\"eventId\":\"evt_2\"}"), 1000));
    [Fact] public void WrongSecretFails() => Assert.False(CallbackService.Verify(Secret, Header(1000, "whsec_other"), Body, 1000));
    [Fact] public void ReplayOutsideTheWindowFails() => Assert.False(CallbackService.Verify(Secret, Header(1000), Body, 1000 + 301));
    [Fact] public void ChangingTheTimestampBreaksTheSignature() =>
        Assert.False(CallbackService.Verify(Secret, Header(1000).Replace("t=1000", "t=2000"), Body, 2000));
    [Theory, InlineData(null), InlineData(""), InlineData("v1=abc"), InlineData("t=x,v1=abc"), InlineData("t=1000")]
    public void MalformedHeadersFail(string? h) => Assert.False(CallbackService.Verify(Secret, h, Body, 1000));

    [Fact]
    public void BackoffIsExponentialAndCapped()
    {
        Assert.Equal(new[] { 10d, 30, 90, 270, 810, 2430, 3600, 3600 }, Enumerable.Range(1, 8).Select(n => CallbackService.RetryDelay(n).TotalSeconds));
        Assert.Equal(8, CallbackService.MaxAttempts);
    }
    [Theory, InlineData(null, true), InlineData(500, true), InlineData(503, true), InlineData(429, true), InlineData(408, true), InlineData(400, false), InlineData(401, false), InlineData(404, false), InlineData(301, false)]
    public void RetryOn(int? status, bool retry) => Assert.Equal(retry, CallbackService.ShouldRetry(status));
}

public class CallbackUrlPolicyTests
{
    static readonly CallbackUrlPolicy Release = new(false), Dev = new(true);

    [Theory]
    [InlineData("https://shop.example.com/btcpay/callback?k=1", true)]
    [InlineData("https://203.0.114.10/cb", true)]
    [InlineData("http://shop.example.com/cb", false)]
    [InlineData("ftp://shop.example.com/cb", false)]
    [InlineData("https://localhost/cb", false)]
    [InlineData("https://api.localhost/cb", false)]
    [InlineData("https://printer.local/cb", false)]
    [InlineData("https://db.internal/cb", false)]
    [InlineData("https://intranet/cb", false)]
    [InlineData("https://127.0.0.1/cb", false)]
    [InlineData("https://10.1.2.3/cb", false)]
    [InlineData("https://172.20.0.1/cb", false)]
    [InlineData("https://192.168.1.1/cb", false)]
    [InlineData("https://169.254.169.254/latest/meta-data", false)]
    [InlineData("https://100.64.0.1/cb", false)]
    [InlineData("https://0.0.0.0/cb", false)]
    [InlineData("https://[::1]/cb", false)]
    [InlineData("https://[fd00::1]/cb", false)]
    [InlineData("https://[fe80::1]/cb", false)]
    [InlineData("https://[::ffff:127.0.0.1]/cb", false)]
    [InlineData("https://user:pw@shop.example.com/cb", false)]
    [InlineData("https://abcdefghijklmnop.onion/cb", false)]
    [InlineData("not a url", false)]
    public void ReleaseRules(string url, bool ok) => Assert.Equal(ok, Release.Validate(url, out _) is null);

    [Theory, InlineData("http://127.0.0.1:5000/cb"), InlineData("http://localhost:5000/cb"), InlineData("https://10.0.0.5/cb")]
    public void DebugBuildsAllowLocalReceivers(string url) => Assert.Null(Dev.Validate(url, out _));

    [Theory, InlineData("8.8.8.8", false), InlineData("1.1.1.1", false), InlineData("2606:4700::1111", false),
     InlineData("127.0.0.53", true), InlineData("224.0.0.1", true), InlineData("255.255.255.255", true), InlineData("64:ff9b::a00:1", true), InlineData("64:ff9b::808:808", false)]
    public void AddressClassification(string ip, bool blocked) => Assert.Equal(blocked, CallbackUrlPolicy.IsBlocked(IPAddress.Parse(ip)));

    [Fact]
    public async Task ReleaseTransportRefusesToDialLoopbackEvenIfAskedTo()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        var t = new HttpCallbackTransport(new OneClientFactory(Release));
        var r = await t.Post(new Uri($"http://127.0.0.1:{port}/cb"), [1], new Dictionary<string, string>(), default);
        Assert.Null(r.Status);
        Assert.Contains("private", r.Error);
        Assert.False(l.Pending()); // nothing was dialled
    }

    [Fact]
    public async Task RedirectsAreNotFollowed()
    {
        await using var rx = new Receiver();
        rx.Respond = _ => (302, rx.Url + "elsewhere");
        var r = await new HttpCallbackTransport(new OneClientFactory(Dev)).Post(new Uri(rx.Url + "cb"), [1], new Dictionary<string, string>(), default);
        Assert.Equal(302, r.Status);
        Assert.Single(rx.Requests);
    }

    [Fact]
    public void TheDevExceptionIsTiedToTheDebugBuild()
    {
#if DEBUG
        Assert.True(CallbackUrlPolicy.DevExceptionCompiledIn);
#else
        Assert.False(CallbackUrlPolicy.DevExceptionCompiledIn);
#endif
        Assert.Equal(CallbackUrlPolicy.DevExceptionCompiledIn, CallbackUrlPolicy.Default.AllowPrivateHosts);
    }
}

class OneClientFactory(CallbackUrlPolicy p) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(HttpCallbackTransport.CreateHandler(p)) { Timeout = HttpCallbackTransport.Timeout };
}

/// <summary>Local HTTP receiver on 127.0.0.1 recording every request.</summary>
sealed class Receiver : IAsyncDisposable
{
    readonly HttpListener _l = new();
    readonly CancellationTokenSource _cts = new();
    public readonly ConcurrentQueue<(Dictionary<string, string> headers, byte[] body)> Requests = new();
    public Func<int, (int status, string? location)> Respond = _ => (200, null);
    public string Url { get; }
    public Receiver()
    {
        var port = FreePort();
        Url = $"http://127.0.0.1:{port}/";
        _l.Prefixes.Add(Url); _l.Start();
        _ = Task.Run(Loop);
    }
    static int FreePort() { using var t = new TcpListener(IPAddress.Loopback, 0); t.Start(); return ((IPEndPoint)t.LocalEndpoint).Port; }
    async Task Loop()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext c;
            try { c = await _l.GetContextAsync(); } catch { return; }
            using var ms = new MemoryStream(); await c.Request.InputStream.CopyToAsync(ms);
            Requests.Enqueue((c.Request.Headers.AllKeys.ToDictionary(k => k!, k => c.Request.Headers[k]!, StringComparer.OrdinalIgnoreCase), ms.ToArray()));
            var (status, loc) = Respond(Requests.Count);
            c.Response.StatusCode = status;
            if (loc is not null) c.Response.RedirectLocation = loc;
            c.Response.Close();
        }
    }
    public ValueTask DisposeAsync() { _cts.Cancel(); try { _l.Stop(); _l.Close(); } catch { } return ValueTask.CompletedTask; }
}

class CapturingLogger<T> : ILogger<T>
{
    public readonly ConcurrentQueue<string> Lines = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel l) => true;
    public void Log<TState>(LogLevel l, EventId id, TState s, Exception? e, Func<TState, Exception?, string> f) => Lines.Enqueue(f(s, e) + e);
}

class FakeSubscriptions : ISubscriptionFacts
{
    public Task<SubscriptionFacts> Resolve(string storeId, string invoiceId, string? planId, string? subscriberId, CancellationToken ct) =>
        Task.FromResult(new SubscriptionFacts(planId, planId is null ? null : "offering_" + planId, subscriberId ?? (planId is null ? null : "42"), planId is null ? null : "cust_1"));
}

[Trait("Category", "Integration")]
public class CallbackIntegrationTests : IAsyncLifetime
{
    const string Server = "Host=127.0.0.1;Port=39372;User ID=postgres";
    readonly string _db = "maxiaff_cb_" + Guid.NewGuid().ToString("N")[..8];
    readonly FixedClock _clock = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
    readonly CapturingLogger<CallbackService> _log = new();
    readonly Receiver _rx = new();
    CallbackService _cb = null!; Factory _f = null!; bool _ok; string _secret = null!;
    class Factory(DbContextOptions<MaxiAffiliatesDbContext> o) : IDbContextFactory<MaxiAffiliatesDbContext> { public MaxiAffiliatesDbContext CreateDbContext() => new(o); }

    public async ValueTask InitializeAsync()
    {
        try { await using var a = new NpgsqlConnection(Server + ";Database=postgres"); await a.OpenAsync(); await new NpgsqlCommand($"CREATE DATABASE \"{_db}\"", a).ExecuteNonQueryAsync(); _ok = true; }
        catch (Exception) { return; }
        var o = new DbContextOptionsBuilder<MaxiAffiliatesDbContext>().UseNpgsql($"{Server};Database={_db};Maximum Pool Size=30").Options;
        await using (var c = new MaxiAffiliatesDbContext(o)) await c.Database.MigrateAsync();
        _f = new Factory(o);
        var dev = new CallbackUrlPolicy(true);
        _cb = new CallbackService(_f, _clock, new EphemeralDataProtectionProvider(), new HttpCallbackTransport(new OneClientFactory(dev)), new FakeSubscriptions(), dev, _log);
        _secret = await _cb.RegenerateSecret("s1");
        Assert.Null(await _cb.SaveSettings("s1", true, _rx.Url + "orders/confirm"));
    }
    public async ValueTask DisposeAsync()
    {
        await _rx.DisposeAsync();
        if (!_ok) return; NpgsqlConnection.ClearAllPools();
        await using var a = new NpgsqlConnection(Server + ";Database=postgres"); await a.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_db}\" WITH (FORCE)", a).ExecuteNonQueryAsync();
    }
    static CallbackFacts Facts(string inv, string? code = "AFF1", bool credited = true, string? plan = "plan_1") =>
        new(inv, 25.50m, "USD", plan, null, code, credited, credited ? "credited" : "no_referral", 1_792_000_000);
    async Task<CallbackDelivery> Delivery(string id) { await using var db = _f.CreateDbContext(); return await db.CallbackDeliveries.AsNoTracking().FirstAsync(d => d.Id == id); }
    async Task<CallbackDelivery[]> All(string store = "s1") => await _cb.ListDeliveries(store);
    async Task Due() { _clock.Now += TimeSpan.FromSeconds(5); await _cb.ProcessDue(); }

    [Fact]
    public async Task SettledEventIsDeliveredSignedWithTheFullPayload()
    {
        Assert.SkipUnless(_ok, "Postgres not available");
        Assert.True(await _cb.EnqueueSettled("s1", Facts("inv_a")));
        await Due();
        var (h, body) = Assert.Single(_rx.Requests);
        Assert.True(CallbackService.Verify(_secret, h[CallbackService.SignatureHeader], body, _clock.Now.ToUnixTimeSeconds()));
        Assert.False(CallbackService.Verify("whsec_wrong", h[CallbackService.SignatureHeader], body, _clock.Now.ToUnixTimeSeconds()));
        var j = JsonDocument.Parse(body).RootElement;
        Assert.Equal("evt_settled_inv_a", j.GetProperty("eventId").GetString());
        Assert.Equal("evt_settled_inv_a", h[CallbackService.EventIdHeader]);
        Assert.Equal("invoice.settled", j.GetProperty("type").GetString());
        Assert.Equal("inv_a", j.GetProperty("invoiceId").GetString());
        Assert.Equal("plan_1", j.GetProperty("planId").GetString());
        Assert.Equal("offering_plan_1", j.GetProperty("offeringId").GetString());
        Assert.Equal("42", j.GetProperty("subscriberId").GetString());
        Assert.Equal("cust_1", j.GetProperty("customerId").GetString());
        Assert.Equal("25.50", j.GetProperty("amount").GetString());
        Assert.Equal("USD", j.GetProperty("currency").GetString());
        Assert.Equal("settled", j.GetProperty("status").GetString());
        Assert.Equal("AFF1", j.GetProperty("affiliateCode").GetString());
        Assert.True(j.GetProperty("commissionCredited").GetBoolean());
        Assert.Equal(1_792_000_000, j.GetProperty("timestamp").GetInt64());
        var d = Assert.Single(await All());
        Assert.Equal(CallbackDeliveryStatus.Succeeded, d.Status);
        Assert.Equal(200, d.LastHttpStatus);
        Assert.Equal(1, d.Attempts);
    }

    [Fact]
    public async Task NoAffiliateGivesNullCodeAndNotCredited()
    {
        Assert.SkipUnless(_ok, "Postgres not available");
        await _cb.EnqueueSettled("s1", Facts("inv_n", code: null, credited: false, plan: null));
        await Due();
        var j = JsonDocument.Parse(Assert.Single(_rx.Requests).body).RootElement;
        Assert.Equal(JsonValueKind.Null, j.GetProperty("affiliateCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, j.GetProperty("subscriberId").ValueKind);
        Assert.False(j.GetProperty("commissionCredited").GetBoolean());
        Assert.Equal("no_referral", j.GetProperty("commissionOutcome").GetString());
    }

    [Fact]
    public async Task ServerErrorIsRetriedWithBackoffThenSucceedsWithTheSameBody()
    {
        Assert.SkipUnless(_ok, "Postgres not available");
        _rx.Respond = n => (n == 1 ? 500 : 200, null);
        await _cb.EnqueueSettled("s1", Facts("inv_r"));
        await Due();
        var d = Assert.Single(await All());
        Assert.Equal(CallbackDeliveryStatus.Pending, d.Status);
        Assert.Equal(500, d.LastHttpStatus);
        Assert.Equal(_clock.Now + TimeSpan.FromSeconds(10), d.NextAttemptAt);
        _clock.Now += TimeSpan.FromSeconds(5); await _cb.ProcessDue();
        Assert.Single(_rx.Requests); // not due yet
        await Due();
        Assert.Equal(2, _rx.Requests.Count);
        var r = _rx.Requests.ToArray();
        Assert.Equal(r[0].body, r[1].body);
        Assert.NotEqual(r[0].headers[CallbackService.SignatureHeader], r[1].headers[CallbackService.SignatureHeader]); // fresh timestamp
        d = Assert.Single(await All());
        Assert.Equal(CallbackDeliveryStatus.Succeeded, d.Status);
        Assert.Equal(2, d.Attempts);
    }

    [Fact]
    public async Task RetriesStopAtTheCap()
    {
        Assert.SkipUnless(_ok, "Postgres not available");
        _rx.Respond = _ => (503, null);
        await _cb.EnqueueSettled("s1", Facts("inv_cap"));
        for (var i = 0; i < 20; i++) { _clock.Now += TimeSpan.FromHours(1); await _cb.ProcessDue(); }
        var d = Assert.Single(await All());
        Assert.Equal(CallbackDeliveryStatus.Failed, d.Status);
        Assert.Equal(CallbackService.MaxAttempts, d.Attempts);
        Assert.Equal(CallbackService.MaxAttempts, _rx.Requests.Count);
    }

    [Fact]
    public async Task ClientErrorIsFinal()
    {
        Assert.SkipUnless(_ok, "Postgres not available");
        _rx.Respond = _ => (400, null);
        await _cb.EnqueueSettled("s1", Facts("inv_400"));
        await Due(); _clock.Now += TimeSpan.FromHours(2); await _cb.ProcessDue();
        var d = Assert.Single(await All());
        Assert.Equal(CallbackDeliveryStatus.Failed, d.Status);
        Assert.Single(_rx.Requests);
    }

    [Fact]
    public async Task DuplicateSettleEventsAndConcurrentWorkersDeliverOnce()
    {
        Assert.SkipUnless(_ok, "Postgres not available");
        var enq = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => _cb.EnqueueSettled("s1", Facts("inv_dup"))));
        Assert.Equal(1, enq.Count(x => x));
        _clock.Now += TimeSpan.FromSeconds(5);
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => _cb.ProcessDue()));
        Assert.Single(_rx.Requests);
        Assert.Single(await All());
    }

    [Fact]
    public async Task RedeliveryRepeatsTheEventWithTheSameIdAndBody()
    {
        Assert.SkipUnless(_ok, "Postgres not available");
        await _cb.EnqueueSettled("s1", Facts("inv_re"));
        await Due();
        var first = Assert.Single(await All());
        Assert.Null(await _cb.Redeliver("s2", first.Id)); // other store
        var id = await _cb.Redeliver("s1", first.Id);
        await _cb.ProcessDue();
        var r = _rx.Requests.ToArray();
        Assert.Equal(2, r.Length);
        Assert.Equal(r[0].body, r[1].body);
        Assert.Equal(r[0].headers[CallbackService.EventIdHeader], r[1].headers[CallbackService.EventIdHeader]);
        Assert.Equal(id, r[1].headers[CallbackService.DeliveryIdHeader]);
        var re = await Delivery(id!);
        Assert.Equal(first.Id, re.RedeliveryOfId);
        Assert.Equal(CallbackDeliveryStatus.Succeeded, re.Status);
    }

    [Fact]
    public async Task TestEventAndSecretHandling()
    {
        Assert.SkipUnless(_ok, "Postgres not available");
        var (id, err) = await _cb.EnqueueTest("s1");
        Assert.Null(err);
        await _cb.ProcessDue();
        var (h, body) = Assert.Single(_rx.Requests);
        var j = JsonDocument.Parse(body).RootElement;
        Assert.True(j.GetProperty("test").GetBoolean());
        Assert.Equal("test", j.GetProperty("type").GetString());
        Assert.True(CallbackService.Verify(_secret, h[CallbackService.SignatureHeader], body, _clock.Now.ToUnixTimeSeconds()));
        Assert.Equal(CallbackDeliveryStatus.Succeeded, (await Delivery(id!)).Status);

        // secret is encrypted at rest and never logged
        await using (var db = _f.CreateDbContext())
        {
            var s = await db.CallbackSettings.AsNoTracking().FirstAsync(x => x.StoreId == "s1");
            Assert.DoesNotContain(_secret, s.SecretProtected);
            Assert.DoesNotContain(_secret["whsec_".Length..], s.SecretProtected);
        }
        Assert.NotEmpty(_log.Lines);
        Assert.DoesNotContain(_log.Lines, l => l.Contains(_secret["whsec_".Length..]));
        Assert.DoesNotContain(_log.Lines, l => l.Contains("/orders/confirm"));

        // regenerating invalidates the old secret
        var newSecret = await _cb.RegenerateSecret("s1");
        Assert.NotEqual(_secret, newSecret);
        await _cb.EnqueueTest("s1"); await _cb.ProcessDue();
        var (h2, b2) = _rx.Requests.ToArray()[1];
        Assert.False(CallbackService.Verify(_secret, h2[CallbackService.SignatureHeader], b2, _clock.Now.ToUnixTimeSeconds()));
        Assert.True(CallbackService.Verify(newSecret, h2[CallbackService.SignatureHeader], b2, _clock.Now.ToUnixTimeSeconds()));
    }

    [Fact]
    public async Task DisabledOrUnconfiguredStoresSendNothing()
    {
        Assert.SkipUnless(_ok, "Postgres not available");
        Assert.False(await _cb.EnqueueSettled("s2", Facts("inv_s2")));
        Assert.Equal("Generate a signing secret before enabling the callback.", await _cb.SaveSettings("s2", true, "https://shop.example.com/cb"));
        Assert.Null(await _cb.SaveSettings("s1", false, _rx.Url + "orders/confirm"));
        Assert.False(await _cb.EnqueueSettled("s1", Facts("inv_off")));
        Assert.Empty(await All());
        Assert.Empty(_rx.Requests);
    }

    [Fact]
    public async Task ReleasePolicyRejectsPrivateUrlsOnSave()
    {
        Assert.SkipUnless(_ok, "Postgres not available");
        var release = new CallbackService(_f, _clock, new EphemeralDataProtectionProvider(), new HttpCallbackTransport(new OneClientFactory(new(false))), new FakeSubscriptions(), new(false), _log);
        await release.RegenerateSecret("s3");
        Assert.Equal("The callback URL must use https.", await release.SaveSettings("s3", true, _rx.Url + "cb"));
        Assert.Equal("The callback URL must point to a public host.", await release.SaveSettings("s3", true, "https://127.0.0.1/cb"));
        Assert.Null(await release.SaveSettings("s3", true, "https://shop.example.com/cb"));
    }
}
