using RustPlusApi.Extensions;
using RustPlusContracts;
using Xunit;
using static RustPlusContracts.AppMarker;

namespace RustPlusApi.UnitTests;

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
        Assert.Equal(0, m.RawType);
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
        Assert.Equal("M", m.Name);
        var item = Assert.Single(m.VendingMachineItems!);
        Assert.Equal(1, item.Id);
        Assert.Equal(1.25f, item.PriceMultiplier);
        Assert.True(item.IsItemBlueprint);
    }

    [Fact]
    public void ToVendingMachineItem_WhenPriceMultiplierUnset_IsNull()
    {
        // PriceMultiplier left default => ShouldSerializePriceMultiplier() is false => null.
        var order = new SellOrder
        {
            ItemId = 9, Quantity = 1, CostPerItem = 1, AmountInStock = 1
        };
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
            AppMarkerType.CargoShip => (marker.ToCargoShipMarker().Id, marker.ToCargoShipMarker().X,
                marker.ToCargoShipMarker().Y),
            AppMarkerType.PatrolHelicopter => (marker.ToPatrolHelicopterMarker().Id,
                marker.ToPatrolHelicopterMarker().X, marker.ToPatrolHelicopterMarker().Y),
            _ => (marker.ToTravellingVendorMarker().Id, marker.ToTravellingVendorMarker().X,
                marker.ToTravellingVendorMarker().Y),
        };
        Assert.Equal(7u, id);
        Assert.Equal(1.5f, x);
        Assert.Equal(2.5f, y);
    }

    [Fact]
    public void ToMarkerColor_MapsComponents()
    {
        var color = new Vector4
        {
            X = 0.1f, Y = 0.2f, Z = 0.3f, W = 0.4f
        };

        var m = color.ToMarkerColor();

        Assert.Equal(0.1f, m.R);
        Assert.Equal(0.2f, m.G);
        Assert.Equal(0.3f, m.B);
        Assert.Equal(0.4f, m.A);
    }

    [Fact]
    public void ToMarkerColor_UnsetComponents_AreNull()
    {
        var m = new Vector4().ToMarkerColor();

        Assert.Null(m.R);
        Assert.Null(m.G);
        Assert.Null(m.B);
        Assert.Null(m.A);
    }

    private static float? MapRotation(AppMarker marker, AppMarkerType type) => type switch
    {
        AppMarkerType.Ch47 => marker.ToCh47Marker().Rotation,
        AppMarkerType.CargoShip => marker.ToCargoShipMarker().Rotation,
        AppMarkerType.PatrolHelicopter => marker.ToPatrolHelicopterMarker().Rotation,
        _ => marker.ToTravellingVendorMarker().Rotation,
    };

    [Theory]
    [InlineData(AppMarkerType.Ch47)]
    [InlineData(AppMarkerType.CargoShip)]
    [InlineData(AppMarkerType.PatrolHelicopter)]
    [InlineData(AppMarkerType.TravellingVendor)]
    public void MovingMarkers_RotationPresent_MapsValue(AppMarkerType type)
    {
        var marker = Marker(type);
        marker.Rotation = 123.5f;

        Assert.Equal(123.5f, MapRotation(marker, type));
    }

    [Theory]
    [InlineData(AppMarkerType.Ch47)]
    [InlineData(AppMarkerType.CargoShip)]
    [InlineData(AppMarkerType.PatrolHelicopter)]
    [InlineData(AppMarkerType.TravellingVendor)]
    public void MovingMarkers_RotationAbsent_MapsNull(AppMarkerType type)
        => Assert.Null(MapRotation(Marker(type), type));

    [Fact]
    public void ToPlayerMarker_UnsetOptionals_AreNull()
    {
        var m = new AppMarker
        {
            Id = 1, X = 0, Y = 0, Type = AppMarkerType.Player
        }.ToPlayerMarker();

        Assert.Null(m.SteamId);
        Assert.Null(m.Name);
    }

    [Fact]
    public void ToVendingMachineMarker_UnsetOptionals_AreNull()
    {
        var m = new AppMarker
        {
            Id = 1, X = 0, Y = 0, Type = AppMarkerType.VendingMachine
        }.ToVendingMachineMarker();

        Assert.Null(m.Name);
        Assert.Null(m.IsOutOfStock);
    }

    [Fact]
    public void ToExplosionMarker_MapsIdAndCoords()
    {
        var m = Marker(AppMarkerType.Explosion).ToExplosionMarker();

        Assert.Equal(7u, m.Id);
        Assert.Equal(1.5f, m.X);
        Assert.Equal(2.5f, m.Y);
    }

    [Fact]
    public void ToCrateMarker_MapsIdAndCoords()
    {
        var m = Marker(AppMarkerType.Crate).ToCrateMarker();

        Assert.Equal(7u, m.Id);
        Assert.Equal(1.5f, m.X);
        Assert.Equal(2.5f, m.Y);
    }

    [Fact]
    public void ToGenericRadiusMarker_MapsStyling()
    {
        var marker = Marker(AppMarkerType.GenericRadius);
        marker.Radius = 25f;
        marker.Alpha = 0.75f;
        marker.Color1 = new Vector4
        {
            X = 1f, Y = 0.5f, Z = 0.25f, W = 1f
        };
        marker.Color2 = new Vector4
        {
            X = 0f, Y = 0f, Z = 0f, W = 0.5f
        };

        var m = marker.ToGenericRadiusMarker();

        Assert.Equal(25f, m.Radius);
        Assert.Equal(0.75f, m.Alpha);
        Assert.Equal(1f, m.Color1!.R);
        Assert.Equal(0.5f, m.Color2!.A);
    }

    [Fact]
    public void ToGenericRadiusMarker_UnsetStyling_IsNull()
    {
        var m = Marker(AppMarkerType.GenericRadius).ToGenericRadiusMarker();

        Assert.Null(m.Radius);
        Assert.Null(m.Alpha);
        Assert.Null(m.Color1);
        Assert.Null(m.Color2);
    }

    [Fact]
    public void ToUnknownMarker_PassesThroughFullSurface()
    {
        var marker = Marker(AppMarkerType.Undefined);
        marker.Rotation = 90f;
        marker.Radius = 10f;
        marker.Alpha = 0.5f;
        marker.Color1 = new Vector4
        {
            X = 1f
        };
        marker.Color2 = new Vector4
        {
            W = 0.5f
        };
        marker.SellOrders.Add(new SellOrder
        {
            ItemId = 1, Quantity = 1, CostPerItem = 1, AmountInStock = 1
        });

        var m = marker.ToUnknownMarker();

        Assert.Equal("M", m.Name);
        Assert.Equal(76561198000000001ul, m.SteamId);
        Assert.True(m.IsOutOfStock);
        Assert.Equal(90f, m.Rotation);
        Assert.Equal(10f, m.Radius);
        Assert.Equal(0.5f, m.Alpha);
        Assert.Equal(1f, m.Color1!.R);
        Assert.Equal(0.5f, m.Color2!.A);
        Assert.Single(m.VendingMachineItems!);
    }

    [Fact]
    public void ToUnknownMarker_UnsetOptionals_AreNull()
    {
        var m = new AppMarker
        {
            Id = 1, X = 0, Y = 0, Type = AppMarkerType.Undefined
        }.ToUnknownMarker();

        Assert.Null(m.Name);
        Assert.Null(m.SteamId);
        Assert.Null(m.IsOutOfStock);
        Assert.Null(m.Rotation);
        Assert.Null(m.Radius);
        Assert.Null(m.Alpha);
        Assert.Null(m.Color1);
        Assert.Null(m.Color2);
        Assert.Empty(m.VendingMachineItems!);
    }
}
