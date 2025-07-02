using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

public class SetSubscription(IRustPlus rustPlus)
{
    public async Task SetSubscriptionAsync(uint entityId, bool doSubscribe)
    {
        var response = await rustPlus.SetSubscriptionAsync(entityId, doSubscribe);
        DisplayUtilities.DisplayJson("SetSubscription", response);
    }
}