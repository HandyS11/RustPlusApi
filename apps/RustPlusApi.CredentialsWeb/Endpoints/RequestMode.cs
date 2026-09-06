using System.Net;
using System.Net.Sockets;

namespace RustPlusApi.CredentialsWeb.Endpoints;

/// <summary>Decides whether a request reached the app from the machine it is running on — or as close
/// to that as this process can tell. That one question settles two things: whether Facepunch's
/// redirect can land here, and whether the visitor is entitled to the pairing wait.
/// <para>Both halves are required. The <c>Host</c> header alone is forgeable, but a forged value only
/// sends the forger's own browser to their own machine, so nothing of ours leaks. The connection
/// address alone is wrong in the deployment that matters: a reverse proxy makes every visitor look
/// alike, which would hand strangers the local behaviour.</para>
/// <para>The connection half deliberately accepts more than loopback, because in the deployment this
/// app leads with — <c>docker run -p 127.0.0.1:8080:8080</c> — a request from the host's own browser
/// never arrives from a loopback address. Docker rewrites the source to the bridge gateway
/// (<c>172.17.0.1</c>, or <c>192.168.65.1</c> on Docker Desktop; <c>10.0.2.x</c> under rootless
/// Podman), so a loopback-only test put the documented local run into the paste flow and then refused
/// it the pairing wait — telling the visitor to run the very command they had just run. Those
/// addresses are all non-routable, so <see cref="IsLocalConnection"/> accepts non-routable space
/// generally rather than trying to enumerate container runtimes.</para>
/// <para>What that widening costs, and who closes it: on a LAN, a peer sending <c>Host: localhost</c>
/// now reads as local and takes the local behaviour, including the pairing wait that
/// <c>AllowRemotePairing</c> exists to gate. No credential is disclosed by that — it is a control
/// bypass bounded by <c>MaxConcurrentPairings</c>, and the same peer is handed a return URL naming
/// their own <c>localhost</c>, so they gain nothing else. A hosted instance configured the way its
/// README requires is unaffected, and is in fact stricter than under the loopback-only rule: with
/// <c>CredentialsWeb:KnownProxies</c> naming the proxy, every visitor presents as their own public
/// address and fails this half outright, where before a same-host proxy made every one of them
/// present as loopback and pass it. With <c>KnownProxies</c> unset behind a proxy the connection half
/// is unconditionally true and <c>Host</c> — a header the caller writes — is all that is left. So a
/// proxied deployment must reject foreign <c>Host</c> values before they reach the app: a named site
/// block at the proxy (a catch-all that passes the client's <c>Host</c> through does not do this), or
/// ASP.NET Core's own host filtering via <c>AllowedHosts</c>, which sits at its <c>*</c> default here
/// because the app ships no <c>appsettings.json</c>. See the app README's reverse-proxy section and
/// <c>Caddyfile.example</c>.</para></summary>
internal static class RequestMode
{
    /// <summary>True when the connection came from an address that is not routable from outside this
    /// machine's own network, and the request names a loopback host.</summary>
    /// <param name="context">The current request.</param>
    internal static bool IsLocal(HttpContext context) =>
        IsLocalConnection(context.Connection.RemoteIpAddress)
        && IsLoopbackHost(context.Request.Host.Host);

    /// <summary>True for a connection address that cannot have been routed here from the internet:
    /// loopback, RFC 1918 private space (<c>10/8</c>, <c>172.16/12</c>, <c>192.168/16</c>), IPv4
    /// link-local (<c>169.254/16</c>), and the IPv6 equivalents. Carrier-grade NAT space
    /// (<c>100.64/10</c>) is deliberately absent: unlike the others it is routed by someone else's
    /// network, so a peer there is not on the visitor's own machine or LAN in any sense this
    /// predicate should treat as local. See the remarks on <see cref="RequestMode"/> for why this is
    /// wider than loopback at all.</summary>
    /// <param name="address">The address to test, or <see langword="null"/> when the request carries
    /// no connection address — as it does under <c>TestServer</c>.</param>
    internal static bool IsLocalConnection(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        var candidate = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        if (IPAddress.IsLoopback(candidate))
        {
            return true;
        }

        if (candidate.AddressFamily != AddressFamily.InterNetwork)
        {
            return candidate.IsIPv6LinkLocal || candidate.IsIPv6UniqueLocal;
        }

        var octets = candidate.GetAddressBytes();
        return octets[0] switch
        {
            10 => true,
            172 => octets[1] is >= 16 and <= 31,
            192 => octets[1] == 168,
            169 => octets[1] == 254,
            _ => false
        };
    }

    /// <summary>True for 127.0.0.0/8, <c>::1</c>, and the IPv4-mapped form of either.
    /// <see cref="IPAddress.IsLoopback"/> rejects <c>::ffff:127.0.0.1</c>, which is exactly what
    /// Kestrel reports on a dual-stack socket, so the mapped form is unwrapped first.</summary>
    /// <param name="address">The address to test, or <see langword="null"/> when the request carries
    /// no connection address — as it does under <c>TestServer</c>.</param>
    internal static bool IsLoopbackAddress(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        var candidate = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        return IPAddress.IsLoopback(candidate);
    }

    /// <summary>True for <c>localhost</c>, any <c>*.localhost</c> — reserved by RFC 6761 and resolved
    /// to loopback by browsers without touching DNS — and any loopback IP literal. This half stays
    /// strictly loopback however wide <see cref="IsLocalConnection"/> is: it is what says the
    /// visitor's browser will resolve the return URL back to its own machine. IPv6 literals arrive
    /// bracketed in a <c>Host</c> header, so brackets are trimmed first.</summary>
    /// <param name="host">The host from the request, without its port.</param>
    internal static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var trimmed = host.Trim('[', ']');

        return string.Equals(trimmed, "localhost", StringComparison.OrdinalIgnoreCase)
               || trimmed.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
               || (IPAddress.TryParse(trimmed, out var parsed) && IsLoopbackAddress(parsed));
    }
}
