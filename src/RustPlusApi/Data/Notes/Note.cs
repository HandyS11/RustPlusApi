namespace RustPlusApi.Data.Notes;

/// <summary>Base record for a map note, carrying the shared map coordinates.</summary>
public record Note
{
    /// <summary>Horizontal map coordinate (west → east).</summary>
    public float X { get; init; }

    /// <summary>Vertical map coordinate (south → north).</summary>
    public float Y { get; init; }
}
