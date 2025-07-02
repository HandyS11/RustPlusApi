using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

public class GetAlarmInfo(IRustPlus rustPlus)
{
    public async Task GetAlarmInfoAsync(uint entityId)
    {
        var response = await rustPlus.GetAlarmInfoAsync(entityId);
        DisplayUtilities.DisplayJson("AlarmInfo", response);
    }
}