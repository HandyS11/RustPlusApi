using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

public class SetSmartSwitchValue(IRustPlus rustPlus)
{
    public async Task SetSmartSwitchValueAsync(uint smartSwitchId, bool smartSwitchValue)
    {
        var response = await rustPlus.SetSmartSwitchValueAsync(smartSwitchId, smartSwitchValue);

        DisplayUtilities.DisplayJson("SetSmartSwitchValue", response);
        if (response.IsSuccess) DisplayUtilities.DisplaySmartSwitchValue(smartSwitchId, smartSwitchValue);
        
    }
}