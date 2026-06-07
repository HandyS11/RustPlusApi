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
    public Dictionary<uint, UnknownMarker> UnknownMarkers { get; init; } = [];

    /// <summary>Player position markers, keyed by marker ID.</summary>
    public Dictionary<uint, PlayerMarker> PlayerMarkers { get; init; } = [];

    /// <summary>Vending machine markers, keyed by marker ID.</summary>
    public Dictionary<uint, VendingMachineMarker> VendingMachineMarkers { get; init; } = [];

    /// <summary>CH-47 (Chinook helicopter) markers, keyed by marker ID.</summary>
    public Dictionary<uint, Ch47Marker> Ch47Markers { get; init; } = [];

    /// <summary>Cargo ship markers, keyed by marker ID.</summary>
    public Dictionary<uint, CargoShipMarker> CargoShipMarkers { get; init; } = [];

    /// <summary>Patrol helicopter markers, keyed by marker ID.</summary>
    public Dictionary<uint, PatrolHelicopterMarker> PatrolHelicopterMarkers { get; init; } = [];
}
