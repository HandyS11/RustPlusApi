using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

public class GetStorageMonitorInfo(IRustPlus rustPlus)
{
    public async Task GetStorageMonitorInfoAsync(uint entityId)
    {
        var response = await rustPlus.GetStorageMonitorInfoAsync(entityId);
        DisplayUtilities.DisplayJson("StorageMonitorInfo", response);
    }
}