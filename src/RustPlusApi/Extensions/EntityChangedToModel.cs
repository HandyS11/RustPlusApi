using RustPlusApi.Data.Events;
using RustPlusContracts;

namespace RustPlusApi.Extensions;

/// <summary>Mapping extensions from protobuf entity-changed broadcasts to event argument types.</summary>
public static class EntityChangedToModel
{
    /// <summary>Maps an <see cref="AppEntityChanged"/> broadcast to a <see cref="SmartDeviceEventArg"/>.
    /// The broadcast carries no entity type, so this represents any binary-state smart device
    /// (a smart switch or a smart alarm).</summary>
    /// <param name="entityChanged">The protobuf entity-changed broadcast.</param>
    public static SmartDeviceEventArg ToSmartDeviceEvent(this AppEntityChanged entityChanged)
    {
        return new SmartDeviceEventArg
        {
            Id = entityChanged.EntityId, IsActive = entityChanged.Payload.Value
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
