using RustPlusApi.Data;

using RustPlusContracts;

namespace RustPlusApi.Extensions;

public static class AppFlagToModel
{
    public static SubscriptionInfo ToSubscriptionInfo(this AppFlag flag)
    {
        return new SubscriptionInfo
        {
            IsSubscribed = flag.Value
        };
    }
}
