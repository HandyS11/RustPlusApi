using RustPlusApi.Extensions;
using RustPlusContracts;
using Xunit;

namespace RustPlusApi.UnitTests;

/// <summary>Locks the marker routing in <see cref="AppMapMarkerToModel.ToMapMarkers"/>:
/// each type lands in the right dictionary and unrecognized types fall back to
/// <c>UnknownMarkers</c> instead of throwing.</summary>
public class MapMarkerDispatchTests
{
    private static AppMapMarkers With(params (ulong id, AppMarkerType type)[] markers)
    {
        var m = new AppMapMarkers();
        foreach (var (id, type) in markers)
        {
            m.Markers.Add(new AppMarker
            {
                Id = id,
                X = 1,
                Y = 1,
                Type = type,
                Name = "n"
            });
        }

        return m;
    }

    [Fact]
    public void ToMapMarkers_RoutesEachKnownTypeToItsDictionary()
    {
        var result = With(
            (1, AppMarkerType.Undefined),
            (2, AppMarkerType.Player),
            (3, AppMarkerType.VendingMachine),
            (4, AppMarkerType.Ch47),
            (5, AppMarkerType.CargoShip),
            (6, AppMarkerType.PatrolHelicopter),
            (7, AppMarkerType.TravellingVendor),
            (8, AppMarkerType.Explosion),
            (9, AppMarkerType.Crate),
            (10, AppMarkerType.GenericRadius)).ToMapMarkers();

        Assert.True(result.UnknownMarkers.ContainsKey(1));
        Assert.True(result.PlayerMarkers.ContainsKey(2));
        Assert.True(result.VendingMachineMarkers.ContainsKey(3));
        Assert.True(result.Ch47Markers.ContainsKey(4));
        Assert.True(result.CargoShipMarkers.ContainsKey(5));
        Assert.True(result.PatrolHelicopterMarkers.ContainsKey(6));
        Assert.True(result.TravellingVendorMarkers.ContainsKey(7));
        Assert.True(result.ExplosionMarkers.ContainsKey(8));
        Assert.True(result.CrateMarkers.ContainsKey(9));
        Assert.True(result.GenericRadiusMarkers.ContainsKey(10));
    }

    [Fact]
    public void ToMapMarkers_UnrecognizedType_FallsBackToUnknown()
    {
        var result = With((1, (AppMarkerType)999)).ToMapMarkers();

        var marker = Assert.Single(result.UnknownMarkers).Value;
        Assert.Equal(1u, marker.Id);
        Assert.Equal(999, marker.RawType);
    }
}
