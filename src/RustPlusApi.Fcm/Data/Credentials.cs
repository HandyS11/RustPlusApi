namespace RustPlusApi.Fcm.Data;

public sealed record Credentials
{
    /// <summary>GCM identity the MCS listener logs in with.</summary>
    public Gcm Gcm { get; init; } = null!;

    /// <summary>The FCM token produced during registration (consumed when registering with Rust Companion).</summary>
    public FcmToken? Fcm { get; init; }

    /// <summary>The Expo push token registered with Rust Companion (<c>ExponentPushToken[...]</c>).</summary>
    public string? ExpoPushToken { get; init; }
}

public sealed record Gcm
{
    public ulong AndroidId { get; init; }
    public ulong SecurityToken { get; init; }
}

public sealed record FcmToken
{
    public string Token { get; init; } = null!;
}
