using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

internal sealed class GetAlarmInfo(IRustPlus rustPlus)
{
    public async Task GetAlarmInfoAsync(ulong entityId)
    {
        var response = await rustPlus.GetAlarmInfoAsync(entityId);
        DisplayUtilities.DisplayJson("AlarmInfo", response);
    }
}
