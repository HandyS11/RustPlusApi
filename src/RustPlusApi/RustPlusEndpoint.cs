using System.Text;

namespace RustPlusApi;

/// <summary>
/// Resolves where a <see cref="RustPlusConnection"/> connects and how it authenticates the WebSocket.
/// Kept apart from <see cref="RustPlusSocket.ConnectAsync"/> so the routing can be tested without
/// reaching Facepunch's hosts.
/// </summary>
internal static class RustPlusEndpoint
{
    /// <summary>Facepunch's companion host, serving both the legacy proxy and the companion socket.</summary>
    internal const string CompanionHost = "companion-rust.facepunch.com";

    /// <summary>The WebSocket subprotocol that announces a companion auth token as the next subprotocol.</summary>
    internal const string AuthSubProtocol = "rustplus.auth.v1";

    /// <summary>
    /// Gets the WebSocket address for <paramref name="connection"/>: the companion socket when a
    /// <see cref="RustPlusConnection.ServerId"/> is set, else the legacy proxy or the server's app port.
    /// </summary>
    /// <param name="connection">The connection to resolve.</param>
    /// <returns>The address to open the WebSocket against.</returns>
    internal static Uri GetUri(RustPlusConnection connection)
    {
        if (connection.ServerId is { } serverId)
        {
            return new Uri($"wss://{CompanionHost}/socket/{serverId:D}");
        }

        return connection.UseFacepunchProxy
            ? new Uri($"wss://{CompanionHost}/game/{connection.Server}/{connection.Port}")
            : new Uri($"ws://{connection.Server}:{connection.Port}");
    }

    /// <summary>
    /// Gets the WebSocket subprotocols for <paramref name="connection"/>. The companion socket takes the
    /// auth token as a subprotocol (browsers cannot set headers on a WebSocket), base64url-encoded so it
    /// is a valid protocol token; the other routes take none.
    /// </summary>
    /// <param name="connection">The connection to resolve.</param>
    /// <returns>The subprotocols to offer, in order; empty when none apply.</returns>
    internal static IReadOnlyList<string> GetSubProtocols(RustPlusConnection connection) =>
        connection.ServerId is null || string.IsNullOrEmpty(connection.AuthToken)
            ? []
            : [AuthSubProtocol, ToBase64Url(connection.AuthToken!)];

    /// <summary>Encodes <paramref name="value"/> as unpadded base64url (RFC 4648 §5) of its UTF-8 bytes.</summary>
    /// <param name="value">The text to encode.</param>
    /// <returns>The base64url form.</returns>
    internal static string ToBase64Url(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
