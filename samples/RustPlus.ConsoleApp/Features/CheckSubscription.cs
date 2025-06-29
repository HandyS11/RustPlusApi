using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

public class CheckSubscription(IRustPlus rustPlus)
{
    public async Task  CheckSubscriptionAsync(uint alarmId)  
    {
        var response = await rustPlus.CheckSubscriptionAsync(alarmId);
        DisplayUtilities.DisplayJson("AlarmInfo", response);
    }
}