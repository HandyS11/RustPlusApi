using RustPlusApi.Data;

using RustPlusContracts;
// ReSharper disable MemberCanBePrivate.Global

namespace RustPlusApi.Extensions
{
    public static class AppEntityInfoToModel
    {
        public static object ToEntityInfo(this AppEntityInfo entity)
        {
            return entity.Type switch
            {
                AppEntityType.Switch => entity.ToSmartSwitchInfo(),
                AppEntityType.Alarm => entity.ToAlarmInfo(),
                AppEntityType.StorageMonitor => entity.ToStorageMonitorInfo(),
                _ => throw new ArgumentException($"The given type is not possible: {entity.Type}")
            };
        }

        public static SmartSwitchInfo ToSmartSwitchInfo(this AppEntityInfo entity)
        {
            return new SmartSwitchInfo
            {
                Value = entity.Payload.Value
            };
        }

        public static AlarmInfo ToAlarmInfo(this AppEntityInfo entity)
        {
            return new AlarmInfo
            {
                Value = entity.Payload.Value
            };
        }

        public static StorageMonitorInfo ToStorageMonitorInfo(this AppEntityInfo entity)
        {
            return new StorageMonitorInfo
            {
                Capacity = entity.Payload.Capacity,
                HasProtection = entity.Payload.HasProtection,
                ProtectionExpiry = DateTimeOffset.FromUnixTimeSeconds(entity.Payload.ProtectionExpiry).UtcDateTime,
                Items = entity.Payload.Items.ToStorageMonitorItemsInfo().ToList()
            };
        }

        public static StorageMonitorItemInfo ToStorageMonitorItemInfo(this AppEntityPayload.Types.Item item)
        {
            return new StorageMonitorItemInfo
            {
                Id = item.ItemId,
                Quantity = item.Quantity,
                IsItemBlueprint = item.ItemIsBlueprint,
            };
        }

        public static IEnumerable<StorageMonitorItemInfo> ToStorageMonitorItemsInfo(this IEnumerable<AppEntityPayload.Types.Item> items)
        {
            return items.Select(ToStorageMonitorItemInfo);
        }
    }
}
