using System.Diagnostics;

using RustPlusApi.Data;
using RustPlusApi.Data.Markers;

using RustPlusContracts;

namespace RustPlusApi.Extensions;

public static class AppMapMarkerToModel
{
    public static MapMarkers ToMapMarkers(this AppMapMarkers appMapMarker)
    {
        Dictionary<uint, UnknownMarker> unknownMarkers = [];
        Dictionary<uint, PlayerMarker> playerMarkers = [];
        // 2. Explosions: doesn't appear anymore in the API
        Dictionary<uint, VendingMachineMarker> vendingMachineMarkers = [];
        Dictionary<uint, Ch47Marker> ch47Markers = [];
        Dictionary<uint, CargoShipMarker> cargoShipMarkers = [];
        // 6. Crates: doesn't appear anymore in the API
        // 7. GenericRadius: I don't know what is this
        Dictionary<uint, PatrolHelicopterMarker> patrolHelicopterMarkers = [];

        foreach (var marker in appMapMarker.Markers)
        {
            switch (marker.Type)
            {
                case AppMarkerType.Unknow:
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
        };
    }
}
