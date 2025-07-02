using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

public class GetSmartSwitchInfo(IRustPlus rustPlus)
{
    public async Task GetSmartSwitchInfoAsync(uint entityId)
    {
        var response = await rustPlus.GetSmartSwitchInfoAsync(entityId);
        DisplayUtilities.DisplayJson("SmartSwitchInfo", response);
    }
}