using RustPlusApi.Data.Entities;
using RustPlusContracts;
// ReSharper disable MemberCanBePrivate.Global

namespace RustPlusApi.Extensions;

/// <summary>Mapping extensions from protobuf entity messages to entity model types.</summary>
public static class AppEntityInfoToModel
{
    /// <summary>Maps an <see cref="AppEntityInfo"/> of type Switch to a <see cref="SmartSwitchInfo"/>.</summary>
    /// <param name="entity">The protobuf entity info.</param>
    /// <exception cref="InvalidOperationException">Thrown when the entity type is not <c>Switch</c>.</exception>
    public static SmartSwitchInfo ToSmartSwitchInfo(this AppEntityInfo entity)
    {
        if (entity.Type is not AppEntityType.Switch)
        {
            throw new InvalidOperationException("Entity type is not a SmartSwitch.");
        }
        return new SmartSwitchInfo
        {
            IsActive = entity.Payload.Value
        };
    }

    /// <summary>Maps an <see cref="AppEntityInfo"/> of type Alarm to an <see cref="AlarmInfo"/>.</summary>
    /// <param name="entity">The protobuf entity info.</param>
    /// <exception cref="InvalidOperationException">Thrown when the entity type is not <c>Alarm</c>.</exception>
    public static AlarmInfo ToAlarmInfo(this AppEntityInfo entity)
    {
        if (entity.Type is not AppEntityType.Alarm)
        {
            throw new InvalidOperationException("Entity type is not an Alarm.");
        }
        return new AlarmInfo
        {
            IsActive = entity.Payload.Value
        };
    }

    /// <summary>Maps an <see cref="AppEntityInfo"/> of type StorageMonitor to a <see cref="StorageMonitorInfo"/>.</summary>
    /// <param name="entity">The protobuf entity info.</param>
    /// <exception cref="InvalidOperationException">Thrown when the entity type is not <c>StorageMonitor</c>.</exception>
    public static StorageMonitorInfo ToStorageMonitorInfo(this AppEntityInfo entity)
    {
        if (entity.Type is not AppEntityType.StorageMonitor)
        {
            throw new InvalidOperationException("Entity type is not a StorageMonitor.");
        }
        return new StorageMonitorInfo
        {
            Capacity = entity.Payload.Capacity,
            HasProtection = entity.Payload.HasProtection,
            ProtectionExpiry = DateTimeOffset.FromUnixTimeSeconds(entity.Payload.ProtectionExpiry).UtcDateTime,
            Items = [.. entity.Payload.Items.ToStorageMonitorItemsInfo()]
        };
    }

    /// <summary>Maps a single protobuf storage item to a <see cref="StorageMonitorItemInfo"/>.</summary>
    /// <param name="item">The protobuf storage item.</param>
    public static StorageMonitorItemInfo ToStorageMonitorItemInfo(this AppEntityPayload.Item item)
    {
        return new StorageMonitorItemInfo
        {
            Id = item.ItemId,
            Quantity = item.Quantity,
            IsItemBlueprint = item.ItemIsBlueprint,
        };
    }

    /// <summary>Maps a sequence of protobuf storage items to <see cref="StorageMonitorItemInfo"/> instances.</summary>
    /// <param name="items">The protobuf storage items to map.</param>
    public static IEnumerable<StorageMonitorItemInfo> ToStorageMonitorItemsInfo(this IEnumerable<AppEntityPayload.Item> items)
    {
        return items.Select(ToStorageMonitorItemInfo);
    }
}
