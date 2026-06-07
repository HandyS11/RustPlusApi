namespace RustPlusApi.Fcm.Data;

/// <summary>FCM and GCM credentials required to connect to the MCS listener and register with Rust+.</summary>
public sealed record Credentials
{
    /// <summary>GCM identity the MCS listener logs in with.</summary>
    public Gcm Gcm { get; init; } = null!;

    /// <summary>The FCM token produced during registration (consumed when registering with Rust Companion).</summary>
    public FcmToken? Fcm { get; init; }

    /// <summary>The Expo push token registered with Rust Companion (<c>ExponentPushToken[...]</c>).</summary>
    public string? ExpoPushToken { get; init; }
}

/// <summary>GCM identity required to authenticate with the MCS listener.</summary>
public sealed record Gcm
{
    /// <summary>GCM-assigned Android device ID.</summary>
    public ulong AndroidId { get; init; }

    /// <summary>GCM-assigned security token paired with <see cref="AndroidId"/>.</summary>
    public ulong SecurityToken { get; init; }
}

/// <summary>FCM registration token produced during Android FCM registration.</summary>
public sealed record FcmToken
{
    /// <summary>The FCM device token string.</summary>
    public string Token { get; init; } = null!;
}
