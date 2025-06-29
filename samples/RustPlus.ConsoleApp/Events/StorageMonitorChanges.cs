using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Events;

public class StorageMonitorChanges(IRustPlus rustPlus)
{
    public void Setup()
    {
        // Subscribe to the StorageMonitorTriggered event
        // This event is triggered when a storage monitor detects changes in the storage
        // IF and ONLY IF the storage monitor state has been checked before (GetStorageMonitorInfo)
        rustPlus.OnStorageMonitorTriggered += (_, message) =>
        {
            DisplayUtilities.DisplayEvent("SmartSwitchChanges", message);
        };
    }
}