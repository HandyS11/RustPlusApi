using RustPlusApi.Data;

using RustPlusContracts;

namespace RustPlusApi.Extensions;

/// <summary>Mapping extensions from protobuf flag messages to model types.</summary>
public static class AppFlagToModel
{
    /// <summary>Maps an <see cref="AppFlag"/> to a <see cref="SubscriptionInfo"/>.</summary>
    /// <param name="flag">The protobuf flag response.</param>
    public static SubscriptionInfo ToSubscriptionInfo(this AppFlag flag)
    {
        return new SubscriptionInfo
        {
            IsSubscribed = flag.Value
        };
    }
}
