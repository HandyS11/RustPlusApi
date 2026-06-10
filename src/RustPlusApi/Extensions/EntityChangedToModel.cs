using RustPlusApi.Data.Events;
using RustPlusContracts;

namespace RustPlusApi.Extensions;

/// <summary>Mapping extensions from protobuf entity-changed broadcasts to event argument types.</summary>
public static class EntityChangedToModel
{
    /// <summary>Maps an <see cref="AppEntityChanged"/> broadcast to a <see cref="SmartSwitchEventArg"/>.</summary>
    /// <param name="entityChanged">The protobuf entity-changed broadcast.</param>
    public static SmartSwitchEventArg ToSmartSwitchEvent(this AppEntityChanged entityChanged)
    {
        return new SmartSwitchEventArg
        {
            Id = entityChanged.EntityId,
            IsActive = entityChanged.Payload.Value
        };
    }

    /// <summary>Maps an <see cref="AppEntityChanged"/> broadcast to a <see cref="StorageMonitorEventArg"/>.</summary>
    /// <param name="entityChanged">The protobuf entity-changed broadcast.</param>
    public static StorageMonitorEventArg ToStorageMonitorEvent(this AppEntityChanged entityChanged)
    {
        return new StorageMonitorEventArg
        {
            Id = entityChanged.EntityId,
            Capacity = entityChanged.Payload.Capacity,
            HasProtection = entityChanged.Payload.HasProtection,
            ProtectionExpiry = DateTimeOffset.FromUnixTimeSeconds(entityChanged.Payload.ProtectionExpiry).UtcDateTime,
            Items = entityChanged.Payload.Items.ToStorageMonitorItemsInfo()
        };
    }
}
