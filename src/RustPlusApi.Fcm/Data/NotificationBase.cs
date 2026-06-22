namespace RustPlusApi.Fcm.Data;

/// <summary>The common envelope for every typed FCM notification: the originating server and the
/// FCM persistent id, so any event can be tied back to its server and de-duplicated/correlated with
/// the id harvested by the socket (see <c>PersistentIdReceived</c> / <c>PersistentIds</c>).</summary>
public abstract record NotificationBase
{
    /// <summary>The Rust+ server ID the notification originated from.</summary>
    public Guid ServerId { get; init; }

    /// <summary>The FCM persistent id of the underlying message; <see langword="null"/> if the
    /// message carried none. Use it to correlate this event with the harvested id set.</summary>
    public string? PersistentId { get; init; }
}
