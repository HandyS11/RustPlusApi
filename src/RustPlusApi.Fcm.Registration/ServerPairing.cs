namespace RustPlusApi.Fcm.Registration;

/// <summary>
/// The strongly-typed result of an in-game "Pair with Server" notification: the arguments for
/// <c>new DirectConnection(Ip, Port, PlayerId, PlayerToken)</c>, or, with the Steam login's auth token,
/// <c>new CompanionConnection(ServerId, authToken, PlayerId, PlayerToken)</c>.
/// </summary>
public sealed record ServerPairing
{
    /// <summary>The server's IP address or hostname.</summary>
    public string Ip { get; init; } = null!;

    /// <summary>The companion app port.</summary>
    public int Port { get; init; }

    /// <summary>The Steam64 ID of the player who paired the server.</summary>
    public ulong PlayerId { get; init; }

    /// <summary>The player's Rust+ authentication token.</summary>
    public int PlayerToken { get; init; }

    /// <summary>The server's display name, if present in the notification.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// Facepunch's id for the server. Pass it to <c>CompanionConnection</c> (with the companion auth
    /// token) to connect through the companion socket, which works where direct connections no longer do.
    /// <see cref="Guid.Empty"/> when the push carried no id, which <c>CompanionConnection</c> refuses.
    /// </summary>
    public Guid ServerId { get; init; }
}
