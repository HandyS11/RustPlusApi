using RustPlusApi.Fcm.Data;

namespace RustPlusApi.Fcm.Data.Events;

/// <summary>A triggered Rust+ smart alarm, on top of the shared <see cref="NotificationBase"/>
/// server/persistent-id envelope. Alarms carry no player context (the server sends none).</summary>
public sealed record AlarmNotification : NotificationBase
{
    /// <summary>The alarm title configured in the Rust+ app.</summary>
    public string Title { get; init; } = null!;

    /// <summary>The alarm message configured in the Rust+ app.</summary>
    public string Message { get; init; } = null!;
}
