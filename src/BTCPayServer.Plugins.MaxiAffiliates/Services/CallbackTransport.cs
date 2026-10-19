using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Plugins.MaxiAffiliates.Services;

public record CallbackHttpResult(int? Status, string? Error);

public interface ICallbackTransport
{
    Task<CallbackHttpResult> Post(Uri url, byte[] body, IReadOnlyDictionary<string, string> headers, CancellationToken ct);
}

/// <summary>The plugin's only outbound HTTP client: seller callbacks, through the address guard of <see cref="CallbackUrlPolicy"/>.</summary>
public class HttpCallbackTransport(IHttpClientFactory factory) : ICallbackTransport
{
    public const string ClientName = "MaxiAffiliates.Callback";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public static SocketsHttpHandler CreateHandler(CallbackUrlPolicy policy) => new()
    {
        AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(10), PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectCallback = async (ctx, ct) =>
        {
            var host = ctx.DnsEndPoint.Host;
            var addresses = IPAddress.TryParse(host.Trim('[', ']'), out var literal) ? new[] { literal } : await Dns.GetHostAddressesAsync(host, ct);
            // Every address the name resolves to must be public, and only those vetted addresses are dialled.
            if (addresses.Length == 0 || addresses.Any(a => !policy.AddressAllowed(a)))
                throw new HttpRequestException("Callback host resolves to a private, loopback or reserved address");
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(addresses, ctx.DnsEndPoint.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch { socket.Dispose(); throw; }
        }
    };

    public async Task<CallbackHttpResult> Post(Uri url, byte[] body, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        var client = factory.CreateClient(ClientName);
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        req.Content = content;
        foreach (var (k, v) in headers) req.Headers.TryAddWithoutValidation(k, v);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);
        try
        {
            using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            return new((int)res.StatusCode, res.IsSuccessStatusCode ? null : $"HTTP {(int)res.StatusCode} {res.ReasonPhrase}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(null, "Timed out"); }
        catch (HttpRequestException e) { return new(null, Short(e.InnerException is SocketException se ? $"Connection failed ({se.SocketErrorCode})" : e.Message)); }
    }

    static string Short(string s) => s.Length <= 200 ? s : s[..200];
}
