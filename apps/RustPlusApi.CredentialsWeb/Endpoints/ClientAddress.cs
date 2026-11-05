using System.Net;
using System.Net.Sockets;

namespace RustPlusApi.CredentialsWeb.Endpoints;

/// <summary>Resolves the caller's address for per-IP accounting.</summary>
internal static class ClientAddress
{
    /// <summary>How much of an IPv6 address identifies one principal: a /64, which is what a single
    /// subscriber or VPS is normally handed.</summary>
    private const int Ipv6PrefixBytes = 8;

    /// <summary>The accounting key for the remote address, already rewritten by the forwarded-headers
    /// middleware when the instance is configured with known proxies. An IPv4-mapped IPv6 address is
    /// keyed as its IPv4 form, and a native IPv6 address as its /64: keyed on the full /128, one
    /// IPv6 subscriber would hold 2^64 independent buckets, enough to fill the global session cap on
    /// its own. Falls back to a constant so accounting still happens (conservatively, as one shared
    /// bucket) rather than silently disappearing. Only per-IP accounting uses this key; the
    /// local-mode decision reads the raw connection address.</summary>
    /// <param name="context">The current request.</param>
    internal static string Of(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, Ipv6PrefixBytes, bytes.Length - Ipv6PrefixBytes);
        return $"{new IPAddress(bytes)}/64";
    }
}
