using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

internal sealed class GetStorageMonitorInfo(IRustPlus rustPlus)
{
    public async Task GetStorageMonitorInfoAsync(ulong entityId)
    {
        var response = await rustPlus.GetStorageMonitorInfoAsync(entityId);
        DisplayUtilities.DisplayJson("StorageMonitorInfo", response);
    }
}
