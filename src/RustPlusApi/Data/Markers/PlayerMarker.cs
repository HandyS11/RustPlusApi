namespace RustPlusApi.Data.Markers;

/// <summary>Map marker for a player's current position.</summary>
public sealed record PlayerMarker : Marker
{
    /// <summary>In-game display name of the player.</summary>
    public string? Name { get; init; }

    /// <summary>Heading in degrees (0–360) as sent by the server, or <see langword="null"/> when omitted.
    /// Consumers own the render transform (the official app draws icons rotated by <c>-Rotation</c> on a Y-down canvas).</summary>
    public float? Rotation { get; init; }

    /// <summary>Steam64 ID of the player.</summary>
    public ulong? SteamId { get; init; }
}
