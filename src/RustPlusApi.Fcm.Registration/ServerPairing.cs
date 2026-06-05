namespace RustPlusApi.Fcm.Registration;

/// <summary>
/// The strongly-typed result of an in-game "Pair with Server" notification — exactly the four
/// arguments needed for <c>new RustPlus(server, port, playerId, playerToken)</c>.
/// </summary>
public sealed record ServerPairing
{
    public string Ip { get; init; } = null!;
    public int Port { get; init; }
    public ulong PlayerId { get; init; }
    public int PlayerToken { get; init; }
    public string? Name { get; init; }
}
