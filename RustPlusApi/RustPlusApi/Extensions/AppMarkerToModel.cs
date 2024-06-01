using RustPlusApi.Data;
using RustPlusApi.Data.Markers;

using RustPlusContracts;

using static RustPlusContracts.AppMarker.Types;

namespace RustPlusApi.Extensions
{
    public static class AppMarkerToModel
    {
        public static UnknownMarker ToUnknownMarker(this AppMarker marker)
        {
            return new UnknownMarker
            {
                Id = marker.Id,
                X = marker.X,
                Y = marker.Y
            };
        }

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
                ItemMaxLife = sellOrder.ItemConditionMax
            };
        }

        public static IEnumerable<VendingMachineItem> ToVendingMachineItems(this IEnumerable<SellOrder> sellOrders)
        {
            return sellOrders.Select(ToVendingMachineItem);
        }

        public static Ch47Marker ToCh47Marker(this AppMarker marker)
        {
            return new Ch47Marker
            {
                Id = marker.Id,
                X = marker.X,
                Y = marker.Y
            };
        }

        public static CargoShipMarker ToCargoShipMarker(this AppMarker marker)
        {
            return new CargoShipMarker
            {
                Id = marker.Id,
                X = marker.X,
                Y = marker.Y
            };
        }

        public static PatrolHelicopterMarker ToPatrolHelicopterMarker(this AppMarker marker)
        {
            return new PatrolHelicopterMarker
            {
                Id = marker.Id,
                X = marker.X,
                Y = marker.Y
            };
        }
    }
}
