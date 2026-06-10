using RustPlusApi.Data;
using RustPlusApi.Data.Markers;
using RustPlusContracts;
using static RustPlusContracts.AppMarker;

// ReSharper disable MemberCanBePrivate.Global

namespace RustPlusApi.Extensions;

/// <summary>Mapping extensions from a protobuf <see cref="AppMarker"/> to typed marker model records.</summary>
public static class AppMarkerToModel
{
    /// <summary>Maps a marker to an <see cref="UnknownMarker"/>.</summary>
    /// <param name="marker">The protobuf map marker.</param>
    public static UnknownMarker ToUnknownMarker(this AppMarker marker)
    {
        return new UnknownMarker
        {
            Id = marker.Id,
            X = marker.X,
            Y = marker.Y
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
            Name = marker.Name,
            SteamId = marker.SteamId
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
            Name = marker.Name,
            IsOutOfStock = marker.OutOfStock,
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
            Y = marker.Y
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
            Y = marker.Y
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
            Y = marker.Y
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
            Y = marker.Y
        };
    }
}
