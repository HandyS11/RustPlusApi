using RustPlusApi.Extensions;
using RustPlusContracts;
using Xunit;
using static RustPlusContracts.AppMarker;

namespace RustPlusApi.Tests.Unit;

/// <summary>Locks every projection in <see cref="AppMarkerToModel"/>, including the sell-order
/// presence fork on <c>PriceMultiplier</c>.</summary>
public class MarkerMapperTests
{
    private static AppMarker Marker(AppMarkerType type) => new()
    {
        Id = 7,
        X = 1.5f,
        Y = 2.5f,
        Name = "M",
        SteamId = 76561198000000001,
        OutOfStock = true,
        Type = type
    };

    [Fact]
    public void ToUnknownMarker_MapsIdAndCoords()
    {
        var m = Marker(AppMarkerType.Undefined).ToUnknownMarker();
        Assert.Equal(7u, m.Id);
        Assert.Equal(1.5f, m.X);
        Assert.Equal(2.5f, m.Y);
    }

    [Fact]
    public void ToPlayerMarker_MapsNameAndSteamId()
    {
        var m = Marker(AppMarkerType.Player).ToPlayerMarker();
        Assert.Equal("M", m.Name);
        Assert.Equal(76561198000000001ul, m.SteamId);
    }

    [Fact]
    public void ToVendingMachineMarker_MapsStockAndSellOrders()
    {
        var marker = Marker(AppMarkerType.VendingMachine);
        marker.SellOrders.Add(new SellOrder
        {
            ItemId = 1,
            Quantity = 2,
            CurrencyId = 3,
            CostPerItem = 4,
            AmountInStock = 5,
            ItemIsBlueprint = true,
            CurrencyIsBlueprint = false,
            ItemCondition = 0.5f,
            ItemConditionMax = 1f,
            PriceMultiplier = 1.25f
        });

        var m = marker.ToVendingMachineMarker();

        Assert.True(m.IsOutOfStock);
        var item = Assert.Single(m.VendingMachineItems!);
        Assert.Equal(1, item.Id);
        Assert.Equal(1.25f, item.PriceMultiplier);
        Assert.True(item.IsItemBlueprint);
    }

    [Fact]
    public void ToVendingMachineItem_WhenPriceMultiplierUnset_IsNull()
    {
        // PriceMultiplier left default => ShouldSerializePriceMultiplier() is false => null.
        var order = new SellOrder { ItemId = 9, Quantity = 1, CostPerItem = 1, AmountInStock = 1 };
        var item = order.ToVendingMachineItem();
        Assert.Null(item.PriceMultiplier);
    }

    [Theory]
    [InlineData(AppMarkerType.Ch47)]
    [InlineData(AppMarkerType.CargoShip)]
    [InlineData(AppMarkerType.PatrolHelicopter)]
    [InlineData(AppMarkerType.TravellingVendor)]
    public void SimpleMarkers_MapCoords(AppMarkerType type)
    {
        var marker = Marker(type);
        var (id, x, y) = type switch
        {
            AppMarkerType.Ch47 => (marker.ToCh47Marker().Id, marker.ToCh47Marker().X, marker.ToCh47Marker().Y),
            AppMarkerType.CargoShip => (marker.ToCargoShipMarker().Id, marker.ToCargoShipMarker().X, marker.ToCargoShipMarker().Y),
            AppMarkerType.PatrolHelicopter => (marker.ToPatrolHelicopterMarker().Id, marker.ToPatrolHelicopterMarker().X, marker.ToPatrolHelicopterMarker().Y),
            _ => (marker.ToTravellingVendorMarker().Id, marker.ToTravellingVendorMarker().X, marker.ToTravellingVendorMarker().Y),
        };
        Assert.Equal(7u, id);
        Assert.Equal(1.5f, x);
        Assert.Equal(2.5f, y);
    }
}
