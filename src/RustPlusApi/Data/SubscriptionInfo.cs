namespace RustPlusApi.Data;

/// <summary>Subscription state for a smart alarm, returned by <c>CheckSubscriptionAsync</c>.</summary>
public sealed record SubscriptionInfo
{
    /// <summary><see langword="true"/> if push notifications are currently active for the alarm.</summary>
    public bool IsSubscribed { get; init; }
}
