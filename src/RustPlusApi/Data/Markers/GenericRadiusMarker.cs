namespace RustPlusApi.Data.Markers;

/// <summary>Map marker for a generic radius overlay (Rust+ marker type 7), carrying its styling.</summary>
public sealed record GenericRadiusMarker : Marker
{
    /// <summary>Opacity of the overlay (0–1), or <see langword="null"/> when omitted.</summary>
    public float? Alpha { get; init; }

    /// <summary>Primary color of the overlay, or <see langword="null"/> when omitted.</summary>
    public MarkerColor? Color1 { get; init; }

    /// <summary>Secondary color of the overlay, or <see langword="null"/> when omitted.</summary>
    public MarkerColor? Color2 { get; init; }

    /// <summary>Radius of the overlay circle in world units, or <see langword="null"/> when omitted.</summary>
    public float? Radius { get; init; }
}
