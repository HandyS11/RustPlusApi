using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

internal sealed class CheckSubscription(IRustPlus rustPlus)
{
    public async Task CheckSubscriptionAsync(ulong alarmId)
    {
        var response = await rustPlus.CheckSubscriptionAsync(alarmId);
        DisplayUtilities.DisplayJson("AlarmInfo", response);
    }
}
