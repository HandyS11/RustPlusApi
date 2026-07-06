using RustPlusApi.Data;
using RustPlusApi.Data.Markers;
using RustPlusContracts;
using static RustPlusContracts.AppMarker;

// ReSharper disable MemberCanBePrivate.Global

namespace RustPlusApi.Extensions;

/// <summary>Mapping extensions from a protobuf <see cref="AppMarker"/> to typed marker model records.</summary>
public static class AppMarkerToModel
{
    /// <summary>Maps a marker to an <see cref="UnknownMarker"/>, passing through the full raw field surface.</summary>
    /// <param name="marker">The protobuf map marker.</param>
    public static UnknownMarker ToUnknownMarker(this AppMarker marker)
    {
        return new UnknownMarker
        {
            Id = marker.Id,
            X = marker.X,
            Y = marker.Y,
            Name = marker.ShouldSerializeName() ? marker.Name : null,
            SteamId = marker.ShouldSerializeSteamId() ? marker.SteamId : null,
            Rotation = marker.ShouldSerializeRotation() ? marker.Rotation : null,
            Radius = marker.ShouldSerializeRadius() ? marker.Radius : null,
            Color1 = marker.Color1?.ToMarkerColor(),
            Color2 = marker.Color2?.ToMarkerColor(),
            Alpha = marker.ShouldSerializeAlpha() ? marker.Alpha : null,
            IsOutOfStock = marker.ShouldSerializeOutOfStock() ? marker.OutOfStock : null,
            VendingMachineItems = marker.SellOrders.ToVendingMachineItems()
        };
    }

    /// <summary>Maps a marker to a <see cref="PlayerMarker"/>.</summary>
    /// <param name="marker">The protobuf map marker.</param>
    public static PlayerMarker ToPlayerMarker(this AppMarker marker)
    {
        return new PlayerMarker
        {
            Id = marker.Id,
            X = marker.X,
            Y = marker.Y,
            Name = marker.ShouldSerializeName() ? marker.Name : null,
            Rotation = marker.ShouldSerializeRotation() ? marker.Rotation : null,
            SteamId = marker.ShouldSerializeSteamId() ? marker.SteamId : null
        };
    }

    /// <summary>Maps a marker to a <see cref="VendingMachineMarker"/>, including its sell orders.</summary>
    /// <param name="marker">The protobuf map marker.</param>
    public static VendingMachineMarker ToVendingMachineMarker(this AppMarker marker)
    {
        return new VendingMachineMarker
        {
            Id = marker.Id,
            X = marker.X,
            Y = marker.Y,
            Name = marker.ShouldSerializeName() ? marker.Name : null,
            IsOutOfStock = marker.ShouldSerializeOutOfStock() ? marker.OutOfStock : null,
            VendingMachineItems = marker.SellOrders.ToVendingMachineItems()
        };
    }

    /// <summary>Maps a protobuf sell order to a <see cref="VendingMachineItem"/>.</summary>
    /// <param name="sellOrder">The protobuf sell order.</param>
    public static VendingMachineItem ToVendingMachineItem(this SellOrder sellOrder)
    {
        return new VendingMachineItem
        {
            Id = sellOrder.ItemId,
            StackSize = sellOrder.Quantity,
            CurrencyId = sellOrder.CurrencyId,
            CostPerStack = sellOrder.CostPerItem,
            StackSizeAmount = sellOrder.AmountInStock,
            IsItemBlueprint = sellOrder.ItemIsBlueprint,
            IsCurrencyBlueprint = sellOrder.CurrencyIsBlueprint,
            ItemLife = sellOrder.ItemCondition,
            ItemMaxLife = sellOrder.ItemConditionMax,
            PriceMultiplier = sellOrder.ShouldSerializePriceMultiplier() ? sellOrder.PriceMultiplier : null
        };
    }

    /// <summary>Maps a sequence of protobuf sell orders to <see cref="VendingMachineItem"/> instances.</summary>
    /// <param name="sellOrders">The protobuf sell orders to map.</param>
    public static IEnumerable<VendingMachineItem> ToVendingMachineItems(this IEnumerable<SellOrder> sellOrders)
    {
        return sellOrders.Select(ToVendingMachineItem);
    }

    /// <summary>Maps a marker to a <see cref="Ch47Marker"/>.</summary>
    /// <param name="marker">The protobuf map marker.</param>
    public static Ch47Marker ToCh47Marker(this AppMarker marker)
    {
        return new Ch47Marker
        {
            Id = marker.Id,
            X = marker.X,
            Y = marker.Y,
            Rotation = marker.ShouldSerializeRotation() ? marker.Rotation : null
        };
    }

    /// <summary>Maps a marker to a <see cref="CargoShipMarker"/>.</summary>
    /// <param name="marker">The protobuf map marker.</param>
    public static CargoShipMarker ToCargoShipMarker(this AppMarker marker)
    {
        return new CargoShipMarker
        {
            Id = marker.Id,
            X = marker.X,
            Y = marker.Y,
            Rotation = marker.ShouldSerializeRotation() ? marker.Rotation : null
        };
    }

    /// <summary>Maps a marker to a <see cref="PatrolHelicopterMarker"/>.</summary>
    /// <param name="marker">The protobuf map marker.</param>
    public static PatrolHelicopterMarker ToPatrolHelicopterMarker(this AppMarker marker)
    {
        return new PatrolHelicopterMarker
        {
            Id = marker.Id,
            X = marker.X,
            Y = marker.Y,
            Rotation = marker.ShouldSerializeRotation() ? marker.Rotation : null
        };
    }

    /// <summary>Maps a marker to a <see cref="TravellingVendorMarker"/>.</summary>
    /// <param name="marker">The protobuf map marker.</param>
    public static TravellingVendorMarker ToTravellingVendorMarker(this AppMarker marker)
    {
        return new TravellingVendorMarker
        {
            Id = marker.Id,
            X = marker.X,
            Y = marker.Y,
            Rotation = marker.ShouldSerializeRotation() ? marker.Rotation : null
        };
    }

    /// <summary>Maps a marker to an <see cref="ExplosionMarker"/>.</summary>
    /// <param name="marker">The protobuf map marker.</param>
    public static ExplosionMarker ToExplosionMarker(this AppMarker marker)
    {
        return new ExplosionMarker
        {
            Id = marker.Id, X = marker.X, Y = marker.Y
        };
    }

    /// <summary>Maps a marker to a <see cref="CrateMarker"/>.</summary>
    /// <param name="marker">The protobuf map marker.</param>
    public static CrateMarker ToCrateMarker(this AppMarker marker)
    {
        return new CrateMarker
        {
            Id = marker.Id, X = marker.X, Y = marker.Y
        };
    }

    /// <summary>Maps a marker to a <see cref="GenericRadiusMarker"/>, including its styling fields.</summary>
    /// <param name="marker">The protobuf map marker.</param>
    public static GenericRadiusMarker ToGenericRadiusMarker(this AppMarker marker)
    {
        return new GenericRadiusMarker
        {
            Id = marker.Id,
            X = marker.X,
            Y = marker.Y,
            Radius = marker.ShouldSerializeRadius() ? marker.Radius : null,
            Color1 = marker.Color1?.ToMarkerColor(),
            Color2 = marker.Color2?.ToMarkerColor(),
            Alpha = marker.ShouldSerializeAlpha() ? marker.Alpha : null
        };
    }

    /// <summary>Maps a protobuf <see cref="Vector4"/> marker color to a <see cref="MarkerColor"/>.</summary>
    /// <param name="color">The protobuf color vector (RGBA components).</param>
    public static MarkerColor ToMarkerColor(this Vector4 color)
    {
        return new MarkerColor
        {
            R = color.ShouldSerializeX() ? color.X : null,
            G = color.ShouldSerializeY() ? color.Y : null,
            B = color.ShouldSerializeZ() ? color.Z : null,
            A = color.ShouldSerializeW() ? color.W : null
        };
    }
}
