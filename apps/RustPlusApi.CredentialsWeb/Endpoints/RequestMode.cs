using System.Net;

namespace RustPlusApi.CredentialsWeb.Endpoints;

/// <summary>Decides whether a request reached the app from the machine it is running on. That one
/// question settles two things: whether Facepunch's redirect can land here, and whether the visitor
/// is entitled to the pairing wait.
/// <para>Both halves are required. The <c>Host</c> header alone is forgeable, but a forged value
/// only sends the forger's own browser to their own machine, so nothing of ours leaks. The
/// connection address alone is wrong in the deployment that matters: a reverse proxy on the same
/// host makes every visitor look like loopback, which would hand strangers the local behaviour.</para>
/// <para>That same deployment is where this predicate is weakest, and the operator has to close the
/// gap because no code here can. Behind a same-host proxy with <c>CredentialsWeb:KnownProxies</c>
/// unset, the connection half is unconditionally true, so all that is left is <c>Host</c> — a header
/// the caller writes. A remote caller sending <c>Host: localhost</c> then reads as local and takes
/// the local behaviour, including the pairing wait that <c>AllowRemotePairing</c> exists to gate. No
/// credential is disclosed by that; it is a control bypass bounded by
/// <c>MaxConcurrentPairings</c>. So a proxied deployment must reject foreign <c>Host</c> values
/// before they reach the app: a named site block at the proxy (a catch-all that passes the client's
/// <c>Host</c> through does not do this), or ASP.NET Core's own host filtering via
/// <c>AllowedHosts</c>, which sits at its <c>*</c> default here because the app ships no
/// <c>appsettings.json</c>. See the app README's reverse-proxy section and
/// <c>Caddyfile.example</c>.</para></summary>
internal static class RequestMode
{
    /// <summary>True when the connection came from a loopback address and the request names a
    /// loopback host.</summary>
    /// <param name="context">The current request.</param>
    internal static bool IsLocal(HttpContext context) =>
        IsLoopbackAddress(context.Connection.RemoteIpAddress)
        && IsLoopbackHost(context.Request.Host.Host);

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
    /// to loopback by browsers without touching DNS — and any loopback IP literal. IPv6 literals
    /// arrive bracketed in a <c>Host</c> header, so brackets are trimmed first.</summary>
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
