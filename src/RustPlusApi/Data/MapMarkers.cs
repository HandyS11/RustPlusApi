using RustPlusApi.Data.Markers;

namespace RustPlusApi.Data;

/// <summary>Collects all map markers returned by the Rust+ server, keyed by marker ID.</summary>
/// <remarks>
/// Marker type 2 (Explosions), type 6 (Crates) and type 7 (GenericRadius) are not emitted by the
/// current Rust+ API and therefore have no corresponding dictionary here.
/// </remarks>
public sealed record MapMarkers
{
    public Dictionary<uint, UnknownMarker> UnknownMarkers { get; init; } = [];
    public Dictionary<uint, PlayerMarker> PlayerMarkers { get; init; } = [];
    public Dictionary<uint, VendingMachineMarker> VendingMachineMarkers { get; init; } = [];
    public Dictionary<uint, Ch47Marker> Ch47Markers { get; init; } = [];
    public Dictionary<uint, CargoShipMarker> CargoShipMarkers { get; init; } = [];
    public Dictionary<uint, PatrolHelicopterMarker> PatrolHelicopterMarkers { get; init; } = [];
}
