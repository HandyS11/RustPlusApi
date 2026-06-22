namespace RustPlusApi.Fcm.Data;

/// <summary>Wraps a typed FCM pairing payload with the originating player context, on top of the
/// shared <see cref="NotificationBase"/> server/persistent-id envelope.</summary>
/// <typeparam name="T">The pairing data type (e.g. <see cref="Events.EntityEvent"/>, <see cref="Events.ServerEvent"/>, or <see cref="ulong"/>).</typeparam>
public record Notification<T> : NotificationBase
{
    /// <summary>Steam ID of the player who performed the pairing.</summary>
    public ulong PlayerId { get; init; }

    /// <summary>Rust+ player token for the pairing player.</summary>
    public int PlayerToken { get; init; }

    /// <summary>The typed pairing payload.</summary>
    public T? Data { get; init; }
}
