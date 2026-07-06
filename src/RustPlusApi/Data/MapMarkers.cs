using RustPlusApi.Data.Markers;

namespace RustPlusApi.Data;

/// <summary>Collects all map markers returned by the Rust+ server, keyed by marker ID.
/// Markers of a type this library does not recognize land in <see cref="UnknownMarkers"/>.</summary>
public sealed record MapMarkers
{
    /// <summary>Cargo ship markers, keyed by marker ID.</summary>
    public Dictionary<ulong, CargoShipMarker> CargoShipMarkers { get; init; } = [];

    /// <summary>CH-47 (Chinook helicopter) markers, keyed by marker ID.</summary>
    public Dictionary<ulong, Ch47Marker> Ch47Markers { get; init; } = [];

    /// <summary>Locked crate markers, keyed by marker ID.</summary>
    public Dictionary<ulong, CrateMarker> CrateMarkers { get; init; } = [];

    /// <summary>Explosion markers, keyed by marker ID.</summary>
    public Dictionary<ulong, ExplosionMarker> ExplosionMarkers { get; init; } = [];

    /// <summary>Generic radius overlay markers, keyed by marker ID.</summary>
    public Dictionary<ulong, GenericRadiusMarker> GenericRadiusMarkers { get; init; } = [];

    /// <summary>Patrol helicopter markers, keyed by marker ID.</summary>
    public Dictionary<ulong, PatrolHelicopterMarker> PatrolHelicopterMarkers { get; init; } = [];

    /// <summary>Player position markers, keyed by marker ID.</summary>
    public Dictionary<ulong, PlayerMarker> PlayerMarkers { get; init; } = [];

    /// <summary>Travelling vendor markers, keyed by marker ID.</summary>
    public Dictionary<ulong, TravellingVendorMarker> TravellingVendorMarkers { get; init; } = [];

    /// <summary>Markers of unknown or unrecognised type (full raw surface), keyed by marker ID.</summary>
    public Dictionary<ulong, UnknownMarker> UnknownMarkers { get; init; } = [];

    /// <summary>Vending machine markers, keyed by marker ID.</summary>
    public Dictionary<ulong, VendingMachineMarker> VendingMachineMarkers { get; init; } = [];
}
