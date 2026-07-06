namespace RustPlusApi.Data.Markers;

/// <summary>Marker with an unrecognised or unsupported type. Carries the full raw field surface of
/// the protobuf marker so nothing the server sends for a new marker type is dropped.</summary>
public sealed record UnknownMarker : Marker
{
    /// <summary>Opacity (0–1), or <see langword="null"/> when omitted.</summary>
    public float? Alpha { get; init; }

    /// <summary>Primary color, or <see langword="null"/> when omitted.</summary>
    public MarkerColor? Color1 { get; init; }

    /// <summary>Secondary color, or <see langword="null"/> when omitted.</summary>
    public MarkerColor? Color2 { get; init; }

    /// <summary><see langword="true"/> if the marker reports being out of stock, or <see langword="null"/> when omitted.</summary>
    public bool? IsOutOfStock { get; init; }

    /// <summary>Display name carried by the marker, or <see langword="null"/> when omitted.</summary>
    public string? Name { get; init; }

    /// <summary>Radius in world units, or <see langword="null"/> when omitted.</summary>
    public float? Radius { get; init; }

    /// <summary>Heading in degrees (0–360) as sent by the server, or <see langword="null"/> when omitted.</summary>
    public float? Rotation { get; init; }

    /// <summary>Steam64 ID carried by the marker, or <see langword="null"/> when omitted.</summary>
    public ulong? SteamId { get; init; }

    /// <summary>Sell orders carried by the marker; empty when none were sent.</summary>
    public IEnumerable<VendingMachineItem>? VendingMachineItems { get; init; }
}
