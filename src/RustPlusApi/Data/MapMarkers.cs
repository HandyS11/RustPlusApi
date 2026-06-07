using RustPlusApi.Data.Markers;

namespace RustPlusApi.Data;

/// <summary>Collects all map markers returned by the Rust+ server, keyed by marker ID.</summary>
/// <remarks>
/// Marker type 2 (Explosions), type 6 (Crates) and type 7 (GenericRadius) are not emitted by the
/// current Rust+ API and therefore have no corresponding dictionary here.
/// </remarks>
public sealed record MapMarkers
{
    /// <summary>Markers of unknown or unrecognised type, keyed by marker ID.</summary>
    public Dictionary<ulong, UnknownMarker> UnknownMarkers { get; init; } = [];

    /// <summary>Player position markers, keyed by marker ID.</summary>
    public Dictionary<ulong, PlayerMarker> PlayerMarkers { get; init; } = [];

    /// <summary>Vending machine markers, keyed by marker ID.</summary>
    public Dictionary<ulong, VendingMachineMarker> VendingMachineMarkers { get; init; } = [];

    /// <summary>CH-47 (Chinook helicopter) markers, keyed by marker ID.</summary>
    public Dictionary<ulong, Ch47Marker> Ch47Markers { get; init; } = [];

    /// <summary>Cargo ship markers, keyed by marker ID.</summary>
    public Dictionary<ulong, CargoShipMarker> CargoShipMarkers { get; init; } = [];

    /// <summary>Patrol helicopter markers, keyed by marker ID.</summary>
    public Dictionary<ulong, PatrolHelicopterMarker> PatrolHelicopterMarkers { get; init; } = [];

    /// <summary>Travelling vendor markers, keyed by marker ID.</summary>
    public Dictionary<ulong, TravellingVendorMarker> TravellingVendorMarkers { get; init; } = [];
}
