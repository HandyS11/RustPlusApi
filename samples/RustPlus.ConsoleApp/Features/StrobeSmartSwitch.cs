using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

public class StrobeSmartSwitch(IRustPlus rustPlus)
{
    public async Task StrobeSmartSwitchAsync(uint entityId, int timeoutMilliseconds = 1000, bool value = true)
    {
        var response = await rustPlus.StrobeSmartSwitchAsync(entityId, timeoutMilliseconds, value);
        
        DisplayUtilities.DisplayJson("StrobeSmartSwitch", response);
        if (response.IsSuccess) DisplayUtilities.DisplaySmartSwitchValue(entityId, response.Data!.IsActive);
    }
}