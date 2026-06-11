namespace RustPlusApi.Fcm;

/// <summary>
/// Tuning options for <see cref="RustPlusFcmSocket"/>. All values have sensible defaults; pass an
/// instance to the <see cref="RustPlusFcm"/> constructor only when the defaults don't fit.
/// Configure at construction and treat as fixed afterwards — the client never mutates it.
/// </summary>
public sealed class RustPlusFcmSocketOptions
{
    /// <summary>How often the client sends its own MCS heartbeat ping. NATs and firewalls silently
    /// drop idle TCP mappings; a periodic client ping keeps the mapping alive and provokes traffic
    /// that the inactivity watchdog can observe. Default: 5 minutes.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long the connection may stay silent (no frame received) before it is presumed
    /// dead: <see cref="RustPlusFcmSocket.ErrorOccurred"/> is raised with a
    /// <see cref="TimeoutException"/> and the socket is disconnected. Should comfortably exceed
    /// <see cref="HeartbeatInterval"/> so a single delayed ack doesn't kill a healthy connection.
    /// Default: 12 minutes.</summary>
    public TimeSpan InactivityTimeout { get; set; } = TimeSpan.FromMinutes(12);

    /// <summary>Creates a copy of this instance — the single place that knows every option, so the
    /// per-socket snapshot cannot silently miss a newly added property.</summary>
    internal RustPlusFcmSocketOptions Clone() => new()
    {
        HeartbeatInterval = HeartbeatInterval,
        InactivityTimeout = InactivityTimeout,
    };
}
