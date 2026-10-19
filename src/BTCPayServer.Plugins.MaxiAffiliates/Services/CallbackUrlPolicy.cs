using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace BTCPayServer.Plugins.MaxiAffiliates.Services;

/// <summary>
/// Outbound-request guard for seller callbacks. Release builds: https only, and the host must not be (or resolve to)
/// a loopback, private, link-local, CGNAT, multicast or otherwise reserved address. The check is repeated when the
/// connection is opened, against the addresses actually dialled, so DNS rebinding cannot bypass it. Redirects are not followed.
/// Debug builds (local development) allow http and private hosts so a receiver on the dev machine works.
/// </summary>
public class CallbackUrlPolicy(bool allowPrivateHosts)
{
#if DEBUG
    public const bool DevExceptionCompiledIn = true;
#else
    public const bool DevExceptionCompiledIn = false;
#endif
    public static CallbackUrlPolicy Default { get; } = new(DevExceptionCompiledIn);
    public bool AllowPrivateHosts { get; } = allowPrivateHosts;

    /// <summary>Returns an error message (translation key) or null when the URL is acceptable.</summary>
    public string? Validate(string? url, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(url) || url.Length > 2048 || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u))
            return "Enter a valid callback URL.";
        if (u.Scheme != Uri.UriSchemeHttps && !(AllowPrivateHosts && u.Scheme == Uri.UriSchemeHttp))
            return "The callback URL must use https.";
        if (!string.IsNullOrEmpty(u.UserInfo) || !string.IsNullOrEmpty(u.Fragment))
            return "The callback URL must not contain credentials or a fragment.";
        if (!AllowPrivateHosts)
        {
            var host = u.IdnHost.TrimEnd('.').ToLowerInvariant();
            if (host is "localhost" || host.EndsWith(".localhost") || host.EndsWith(".local") || host.EndsWith(".internal") || host.EndsWith(".onion")
                || u.HostNameType is UriHostNameType.Basic or UriHostNameType.Unknown || !host.Contains('.') && u.HostNameType == UriHostNameType.Dns)
                return "The callback URL must point to a public host.";
            if (IPAddress.TryParse(u.Host.Trim('[', ']'), out var ip) && IsBlocked(ip))
                return "The callback URL must point to a public host.";
        }
        uri = u;
        return null;
    }

    public bool AddressAllowed(IPAddress ip) => AllowPrivateHosts || !IsBlocked(ip);

    public static bool IsBlocked(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.None)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] switch
            {
                0 or 10 or 127 => true,
                100 => b[1] >= 64 && b[1] <= 127,          // 100.64.0.0/10 CGNAT
                169 => b[1] == 254,                         // link-local / cloud metadata
                172 => b[1] >= 16 && b[1] <= 31,
                192 => b[1] == 168 || (b[1] == 0 && (b[2] == 0 || b[2] == 2)) || (b[1] == 88 && b[2] == 99),
                198 => b[1] is 18 or 19 || (b[1] == 51 && b[2] == 100),
                203 => b[1] == 0 && b[2] == 113,
                >= 224 => true,                             // multicast, reserved, broadcast
                _ => false
            };
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = ip.GetAddressBytes();
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return true;
            if ((b[0] & 0xfe) == 0xfc) return true;          // fc00::/7 unique local
            if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8) return true; // documentation
            if (b.Take(12).SequenceEqual(new byte[] { 0, 0x64, 0xff, 0x9b, 0, 0, 0, 0, 0, 0, 0, 0 }))
                return IsBlocked(new IPAddress(b[12..]));    // NAT64: check the embedded IPv4
            return false;
        }
        return true;
    }
}
