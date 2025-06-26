using RustPlusApi.Data.Entities;
using RustPlusContracts;
// ReSharper disable MemberCanBePrivate.Global

namespace RustPlusApi.Extensions;

public static class AppEntityInfoToModel
{
    public static SmartSwitchInfo ToSmartSwitchInfo(this AppEntityInfo entity)
    {
        return new SmartSwitchInfo
        {
            IsActive = entity.Payload.Value
        };
    }

    public static AlarmInfo ToAlarmInfo(this AppEntityInfo entity)
    {
        return new AlarmInfo
        {
            IsActive = entity.Payload.Value
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
