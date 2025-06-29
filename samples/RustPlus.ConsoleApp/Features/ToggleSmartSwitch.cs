using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

public class ToggleSmartSwitch(IRustPlus rustPlus)
{
    public async Task ToggleSmartSwitchAsync(uint entityId)
    {
        var response = await rustPlus.ToggleSmartSwitchAsync(entityId);
        
        DisplayUtilities.DisplayJson("ToggleSmartSwitch", response);
        if (response.IsSuccess) DisplayUtilities.DisplaySmartSwitchValue(entityId, response.Data!.IsActive);
    }
}