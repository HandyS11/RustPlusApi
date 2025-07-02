using RustPlusApi.Data.Markers;

namespace RustPlusApi.Data;

public sealed record MapMarkers
{
    public Dictionary<uint, UnknownMarker> UnknownMarkers { get; init; } = [];
    public Dictionary<uint, PlayerMarker> PlayerMarkers { get; init; } = [];
    // 2. Explosions: doesn't appear anymore in the API
    public Dictionary<uint, VendingMachineMarker> VendingMachineMarkers { get; init; } = [];
    public Dictionary<uint, Ch47Marker> Ch47Markers { get; init; } = [];
    public Dictionary<uint, CargoShipMarker> CargoShipMarkers { get; init; } = [];
    // 6. Crates: doesn't appear anymore in the API
    // 7. GenericRadius: I don't know what is this
    public Dictionary<uint, PatrolHelicopterMarker> PatrolHelicopterMarkers { get; init; } = [];
}
