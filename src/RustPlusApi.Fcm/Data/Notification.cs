namespace RustPlusApi.Fcm.Data;

/// <summary>Wraps a typed FCM pairing payload with the originating player and server context.</summary>
/// <typeparam name="T">The pairing data type (e.g. <see cref="Events.EntityEvent"/>, <see cref="Events.ServerEvent"/>, or <see cref="int"/>).</typeparam>
public record Notification<T>
{
    /// <summary>Steam ID of the player who performed the pairing.</summary>
    public ulong PlayerId { get; init; }

    /// <summary>Rust+ player token for the pairing player.</summary>
    public int PlayerToken { get; init; }

    /// <summary>The Rust+ server ID the pairing is associated with.</summary>
    public Guid ServerId { get; init; }

    /// <summary>The typed pairing payload.</summary>
    public T? Data { get; init; }
}
