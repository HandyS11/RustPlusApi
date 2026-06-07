using System.Diagnostics;

using RustPlusApi.Data;
using RustPlusApi.Data.Markers;

using RustPlusContracts;

namespace RustPlusApi.Extensions;

/// <summary>Mapping extensions from protobuf map-marker messages to model types.</summary>
public static class AppMapMarkerToModel
{
    /// <summary>Maps an <see cref="AppMapMarkers"/> response to a <see cref="MapMarkers"/> model, routing each marker to its typed dictionary.</summary>
    /// <param name="appMapMarker">The protobuf map markers response.</param>
    /// <exception cref="ArgumentException">Thrown when a marker has an unrecognized type.</exception>
    public static MapMarkers ToMapMarkers(this AppMapMarkers appMapMarker)
    {
        Dictionary<ulong, UnknownMarker> unknownMarkers = [];
        Dictionary<ulong, PlayerMarker> playerMarkers = [];
        // 2. Explosions: doesn't appear anymore in the API
        Dictionary<ulong, VendingMachineMarker> vendingMachineMarkers = [];
        Dictionary<ulong, Ch47Marker> ch47Markers = [];
        Dictionary<ulong, CargoShipMarker> cargoShipMarkers = [];
        // 6. Crates: doesn't appear anymore in the API
        // 7. GenericRadius: I don't know what is this
        Dictionary<ulong, PatrolHelicopterMarker> patrolHelicopterMarkers = [];
        Dictionary<ulong, TravellingVendorMarker> travellingVendorMarkers = [];

        foreach (var marker in appMapMarker.Markers)
        {
            switch (marker.Type)
            {
                case AppMarkerType.Undefined:
                    unknownMarkers.Add(marker.Id, marker.ToUnknownMarker());
                    break;
                case AppMarkerType.Player:
                    playerMarkers.Add(marker.Id, marker.ToPlayerMarker());
                    break;
                case AppMarkerType.Explosion:
                    Debug.WriteLine("WTF!! Facepunch acknowledge their mistake?");
                    break;
                case AppMarkerType.VendingMachine:
                    vendingMachineMarkers.Add(marker.Id, marker.ToVendingMachineMarker());
                    break;
                case AppMarkerType.Ch47:
                    ch47Markers.Add(marker.Id, marker.ToCh47Marker());
                    break;
                case AppMarkerType.CargoShip:
                    cargoShipMarkers.Add(marker.Id, marker.ToCargoShipMarker());
                    break;
                case AppMarkerType.Crate:
                    Debug.WriteLine("WTF!! Facepunch acknowledge their mistake?");
                    break;
                case AppMarkerType.GenericRadius:
                    Debug.WriteLine($"What the fuck is that?\n{marker}");
                    break;
                case AppMarkerType.PatrolHelicopter:
                    patrolHelicopterMarkers.Add(marker.Id, marker.ToPatrolHelicopterMarker());
                    break;
                case AppMarkerType.TravellingVendor:
                    travellingVendorMarkers.Add(marker.Id, marker.ToTravellingVendorMarker());
                    break;
                default:
                    throw new ArgumentException($"Unknown marker type: {marker.Type}");
            }
        }

        return new MapMarkers
        {
            UnknownMarkers = unknownMarkers,
            PlayerMarkers = playerMarkers,
            VendingMachineMarkers = vendingMachineMarkers,
            Ch47Markers = ch47Markers,
            CargoShipMarkers = cargoShipMarkers,
            PatrolHelicopterMarkers = patrolHelicopterMarkers,
            TravellingVendorMarkers = travellingVendorMarkers,
        };
    }
}
