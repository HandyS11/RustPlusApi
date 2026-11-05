using System.Text;

namespace RustPlusApi;

/// <summary>
/// Resolves where a <see cref="RustPlusConnection"/> connects and how it authenticates the WebSocket.
/// Kept apart from <see cref="RustPlusSocket.ConnectAsync"/> so the routing can be tested without
/// reaching Facepunch's hosts.
/// </summary>
internal static class RustPlusEndpoint
{
    /// <summary>The WebSocket subprotocol that announces a companion auth token as the next subprotocol.</summary>
    internal const string AuthSubProtocol = "rustplus.auth.v1";

    /// <summary>Facepunch's companion host, serving both the legacy proxy and the companion socket.</summary>
    internal static readonly Uri CompanionBaseUri = new("wss://companion-rust.facepunch.com/");

    /// <summary>
    /// Gets the WebSocket address for <paramref name="connection"/>: the companion socket when a
    /// <see cref="RustPlusConnection.ServerId"/> is set, else the legacy proxy or the server's app port.
    /// </summary>
    /// <param name="connection">The connection to resolve.</param>
    /// <param name="companionBaseUri">The companion host the socket and proxy routes resolve against —
    /// <see cref="CompanionBaseUri"/> outside tests.</param>
    /// <returns>The address to open the WebSocket against.</returns>
    internal static Uri GetUri(RustPlusConnection connection, Uri companionBaseUri)
    {
        if (connection.ServerId is { } serverId)
        {
            return new Uri(companionBaseUri, $"socket/{serverId:D}");
        }

        return connection.UseFacepunchProxy
            ? new Uri(companionBaseUri, $"game/{connection.Server}/{connection.Port}")
            : new Uri($"ws://{connection.Server}:{connection.Port}");
    }

    /// <summary>
    /// Gets the WebSocket subprotocols for <paramref name="connection"/>, validating the companion-socket
    /// settings on the way. The companion socket takes the auth token as a subprotocol (browsers cannot
    /// set headers on a WebSocket), base64url-encoded so it is a valid protocol token; the other routes
    /// take none.
    /// </summary>
    /// <param name="connection">The connection to resolve.</param>
    /// <returns>The subprotocols to offer, in order; empty when none apply.</returns>
    /// <exception cref="ArgumentException"><see cref="RustPlusConnection.ServerId"/> is set but is
    /// <see cref="Guid.Empty"/>, or <see cref="RustPlusConnection.AuthToken"/> is null, empty or
    /// whitespace — the companion socket would reject the handshake with a bare 404 or 403.</exception>
    internal static IReadOnlyList<string> GetSubProtocols(RustPlusConnection connection)
    {
        if (connection.ServerId is not { } serverId)
        {
            return [];
        }

        if (serverId == Guid.Empty)
        {
            throw new ArgumentException(
                $"{nameof(RustPlusConnection.ServerId)} is Guid.Empty; pass the server id from the pairing notification.",
                nameof(connection));
        }

        // A pattern rather than string.IsNullOrWhiteSpace alone: netstandard2.0's reference assembly
        // lacks [NotNullWhen(false)], so only the pattern proves the token non-null on both TFMs.
        if (connection.AuthToken is not { } authToken || string.IsNullOrWhiteSpace(authToken))
        {
            throw new ArgumentException(
                $"{nameof(RustPlusConnection.AuthToken)} is required when {nameof(RustPlusConnection.ServerId)} is set: "
                + "the companion socket authenticates with the Rust+ auth token from the Steam login.",
                nameof(connection));
        }

        return [AuthSubProtocol, ToBase64Url(authToken)];
    }

    /// <summary>Encodes <paramref name="value"/> as unpadded base64url (RFC 4648 §5) of its UTF-8 bytes.</summary>
    /// <param name="value">The text to encode.</param>
    /// <returns>The base64url form.</returns>
    internal static string ToBase64Url(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
