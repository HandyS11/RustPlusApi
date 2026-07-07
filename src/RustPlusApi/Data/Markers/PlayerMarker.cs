namespace RustPlusApi.Data.Markers;

/// <summary>Map marker for a player's current position.</summary>
public sealed record PlayerMarker : Marker
{
    /// <summary>In-game display name of the player, or <see langword="null"/> when omitted.</summary>
    public string? Name { get; init; }

    /// <summary>Steam64 ID of the player, or <see langword="null"/> when omitted.</summary>
    public ulong? SteamId { get; init; }
}
