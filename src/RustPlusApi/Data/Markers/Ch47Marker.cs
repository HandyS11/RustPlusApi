namespace RustPlusApi.Data.Markers;

/// <summary>Map marker for the CH-47 Chinook helicopter.</summary>
public sealed record Ch47Marker : Marker
{
    /// <summary>Heading in degrees (0–360) as sent by the server, or <see langword="null"/> when omitted.
    /// Consumers own the render transform (the official app draws icons rotated by <c>-Rotation</c> on a Y-down canvas).</summary>
    public float? Rotation { get; init; }
}
