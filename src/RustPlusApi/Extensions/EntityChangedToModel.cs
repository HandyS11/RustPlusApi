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

    /// <summary>Maps an <see cref="AppEntityChanged"/> broadcast to the raw
    /// <see cref="EntityChangedEventArg"/>, preserving field presence (absent optional fields map to
    /// <see langword="null"/>).</summary>
    /// <param name="entityChanged">The protobuf entity-changed broadcast.</param>
    public static EntityChangedEventArg ToEntityChangedEvent(this AppEntityChanged entityChanged)
    {
        var payload = entityChanged.Payload;
        return new EntityChangedEventArg
        {
            Id = entityChanged.EntityId,
            Value = payload.ShouldSerializeValue() ? payload.Value : null,
            Capacity = payload.ShouldSerializeCapacity() ? payload.Capacity : null,
            HasProtection = payload.ShouldSerializeHasProtection() ? payload.HasProtection : null,
            ProtectionExpiry = payload.ShouldSerializeProtectionExpiry()
                ? DateTimeOffset.FromUnixTimeSeconds(payload.ProtectionExpiry).UtcDateTime
                : null,
            Items = payload.Items.ToStorageMonitorItemsInfo()
        };
    }
}
