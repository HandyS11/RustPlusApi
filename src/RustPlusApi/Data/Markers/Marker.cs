namespace RustPlusApi.Data.Markers;

/// <summary>Base record for all map markers, carrying the shared id and map coordinates.</summary>
public record Marker
{
    /// <summary>Unique marker identifier assigned by the server.</summary>
    public uint? Id { get; init; }

    /// <summary>Horizontal map coordinate (west → east).</summary>
    public float? X { get; init; }

    /// <summary>Vertical map coordinate (south → north).</summary>
    public float? Y { get; init; }
}
