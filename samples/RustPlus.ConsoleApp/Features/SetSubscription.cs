using RustPlus.ConsoleApp.Utils;
using RustPlusApi.Interfaces;

namespace RustPlus.ConsoleApp.Features;

internal sealed class SetSubscription(IRustPlus rustPlus)
{
    public async Task SetSubscriptionAsync(ulong entityId, bool doSubscribe)
    {
        var response = await rustPlus.SetSubscriptionAsync(entityId, doSubscribe);
        DisplayUtilities.DisplayJson("SetSubscription", response);
    }
}
