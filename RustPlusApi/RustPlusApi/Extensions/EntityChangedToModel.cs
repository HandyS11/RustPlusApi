using RustPlusApi.Data.Events;

using RustPlusContracts;

namespace RustPlusApi.Extensions
{
    public static class EntityChangedToModel
    {
        public static SmartSwitchEventArg ToSmartSwitchEvent(this AppEntityChanged entityChanged)
        {
            return new SmartSwitchEventArg
            {
                Id = entityChanged.EntityId,
                Value = entityChanged.Payload.Value
            };
        }

        public static StorageMonitorEventArg ToStorageMonitorEvent(this AppEntityChanged entityChanged)
        {
            return new StorageMonitorEventArg
            {
                Id = entityChanged.EntityId,
                Capacity = entityChanged.Payload.Capacity,
                HasProtection = entityChanged.Payload.HasProtection,
                ProtectionExpiry = entityChanged.Payload.ProtectionExpiry,
                Items = entityChanged.Payload.Items.ToStorageMonitorItemsInfo().ToList()
            };
        }
    }
}
