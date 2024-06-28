using RustPlusApi.Data.Markers;

namespace RustPlusApi.Data
{
    public class MapMarkers
    {
        public Dictionary<uint, UnknownMarker>? UnknownMarkers { get; set; }
        public Dictionary<uint, PlayerMarker>? PlayerMarkers { get; set; }
        // 2. Explosions: doesn't appear anymore in the API
        public Dictionary<uint, VendingMachineMarker>? VendingMachineMarkers { get; set; }
        public Dictionary<uint, Ch47Marker>? Ch47Markers { get; set; }
        public Dictionary<uint, CargoShipMarker>? CargoShipMarkers { get; set; }
        // 6. Crates: doesn't appear anymore in the API
        // 7. GenericRadius: I don't know what is this
        public Dictionary<uint, PatrolHelicopterMarker>? PatrolHelicopterMarkers { get; set; }
    }
}
