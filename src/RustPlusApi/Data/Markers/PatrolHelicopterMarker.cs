namespace RustPlusApi.Data.Markers;

/// <summary>Map marker for the Patrol Helicopter event.</summary>
public sealed record PatrolHelicopterMarker : Marker
{
    /// <summary>Heading in degrees (0–360) as sent by the server, or <see langword="null"/> when omitted.
    /// Consumers own the render transform (the official app draws icons rotated by <c>-Rotation</c> on a Y-down canvas).</summary>
    public float? Rotation { get; init; }
}
