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

    /// <summary>Facepunch's companion host, which serves the companion socket.</summary>
    internal static readonly Uri CompanionBaseUri = new("wss://companion-rust.facepunch.com/");

    /// <summary>
    /// Gets the WebSocket address for <paramref name="connection"/>: the companion socket for a
    /// <see cref="CompanionConnection"/>, the server's app port for a <see cref="DirectConnection"/>.
    /// </summary>
    /// <param name="connection">The connection to resolve.</param>
    /// <param name="companionBaseUri">The companion host the companion socket resolves against —
    /// <see cref="CompanionBaseUri"/> outside tests.</param>
    /// <returns>The address to open the WebSocket against.</returns>
    /// <exception cref="ArgumentException"><paramref name="connection"/> is neither of the two types.</exception>
    internal static Uri GetUri(RustPlusConnection connection, Uri companionBaseUri) => connection switch
    {
        CompanionConnection companion => new Uri(companionBaseUri, $"socket/{companion.ServerId:D}"),
        DirectConnection direct => new Uri($"ws://{direct.Server}:{direct.Port}"),
        _ => throw Unsupported(connection),
    };

    /// <summary>
    /// Gets the WebSocket subprotocols for <paramref name="connection"/>. The companion socket takes the
    /// auth token as a subprotocol (browsers cannot set headers on a WebSocket), base64url-encoded so it is
    /// a valid protocol token; a direct connection takes none, so the token can never reach a game server.
    /// </summary>
    /// <param name="connection">The connection to resolve.</param>
    /// <returns>The subprotocols to offer, in order; empty when none apply.</returns>
    /// <exception cref="ArgumentException"><paramref name="connection"/> is neither of the two types.</exception>
    internal static IReadOnlyList<string> GetSubProtocols(RustPlusConnection connection) => connection switch
    {
        CompanionConnection companion => [AuthSubProtocol, ToBase64Url(companion.AuthToken)],
        DirectConnection => [],
        _ => throw Unsupported(connection),
    };

    /// <summary>Encodes <paramref name="value"/> as unpadded base64url (RFC 4648 §5) of its UTF-8 bytes.</summary>
    /// <param name="value">The text to encode.</param>
    /// <returns>The base64url form.</returns>
    internal static string ToBase64Url(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static ArgumentException Unsupported(RustPlusConnection connection) => new(
        $"{connection.GetType().Name} is not a supported connection; use a {nameof(DirectConnection)} or a "
        + $"{nameof(CompanionConnection)}.",
        nameof(connection));
}
