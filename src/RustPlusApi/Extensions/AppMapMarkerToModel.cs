using RustPlusApi.Data;
using RustPlusApi.Data.Markers;
using RustPlusContracts;

namespace RustPlusApi.Extensions;

/// <summary>Mapping extensions from protobuf map-marker messages to model types.</summary>
public static class AppMapMarkerToModel
{
    /// <summary>Maps an <see cref="AppMapMarkers"/> response to a <see cref="MapMarkers"/> model, routing each
    /// marker to its typed dictionary. Markers with an unrecognized type fall back to
    /// <see cref="MapMarkers.UnknownMarkers"/> so a game update cannot break the read.</summary>
    /// <param name="appMapMarker">The protobuf map markers response.</param>
    public static MapMarkers ToMapMarkers(this AppMapMarkers appMapMarker)
    {
        var result = new MapMarkers();

        foreach (var marker in appMapMarker.Markers)
        {
            switch (marker.Type)
            {
                case AppMarkerType.Player:
                    result.PlayerMarkers.Add(marker.Id, marker.ToPlayerMarker());
                    break;
                case AppMarkerType.Explosion:
                    result.ExplosionMarkers.Add(marker.Id, marker.ToExplosionMarker());
                    break;
                case AppMarkerType.VendingMachine:
                    result.VendingMachineMarkers.Add(marker.Id, marker.ToVendingMachineMarker());
                    break;
                case AppMarkerType.Ch47:
                    result.Ch47Markers.Add(marker.Id, marker.ToCh47Marker());
                    break;
                case AppMarkerType.CargoShip:
                    result.CargoShipMarkers.Add(marker.Id, marker.ToCargoShipMarker());
                    break;
                case AppMarkerType.Crate:
                    result.CrateMarkers.Add(marker.Id, marker.ToCrateMarker());
                    break;
                case AppMarkerType.GenericRadius:
                    result.GenericRadiusMarkers.Add(marker.Id, marker.ToGenericRadiusMarker());
                    break;
                case AppMarkerType.PatrolHelicopter:
                    result.PatrolHelicopterMarkers.Add(marker.Id, marker.ToPatrolHelicopterMarker());
                    break;
                case AppMarkerType.TravellingVendor:
                    result.TravellingVendorMarkers.Add(marker.Id, marker.ToTravellingVendorMarker());
                    break;
                // AppMarkerType.Undefined and unrecognized future types intentionally route here.
                default:
                    result.UnknownMarkers.Add(marker.Id, marker.ToUnknownMarker());
                    break;
            }
        }

        return result;
    }
}
