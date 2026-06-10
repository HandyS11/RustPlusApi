namespace RustPlusApi.Fcm.Registration;

/// <summary>
/// The strongly-typed result of an in-game "Pair with Server" notification — exactly the four
/// arguments needed for <c>new RustPlus(server, port, playerId, playerToken)</c>.
/// </summary>
public sealed record ServerPairing
{
    /// <summary>The server's IP address or hostname.</summary>
    public string Ip { get; init; } = null!;

    /// <summary>The server's game port.</summary>
    public int Port { get; init; }

    /// <summary>The Steam64 ID of the player who paired the server.</summary>
    public ulong PlayerId { get; init; }

    /// <summary>The player's Rust+ authentication token.</summary>
    public int PlayerToken { get; init; }

    /// <summary>The server's display name, if present in the notification.</summary>
    public string? Name { get; init; }
}
