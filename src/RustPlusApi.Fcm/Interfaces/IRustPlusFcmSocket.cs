namespace RustPlusApi.Fcm.Interfaces;

/// <summary>Defines the connection lifecycle and notification events for a low-level FCM MCS socket.
/// Clients are disposable; prefer <see cref="IAsyncDisposable.DisposeAsync"/> so teardown drains background work.</summary>
public interface IRustPlusFcmSocket : IDisposable, IAsyncDisposable
{
    /// <summary>A never-null snapshot of the <c>persistentId</c>s currently tracked for
    /// de-duplication. Persist and replay these via the constructor to suppress redelivery across
    /// reconnects; ids have a server-side lifespan, so pruning your stored copy is your job.</summary>
    /// <remarks>
    /// <para><b>Thread safety:</b> the snapshot enumerates the caller-owned collection with no lock.
    /// The receive loop adds ids on its own task, so reading <see cref="PersistentIds"/> from an
    /// unrelated thread while live traffic is flowing can throw
    /// <c>InvalidOperationException</c> (collection modified during enumeration).
    /// Safe read points: inside a <see cref="PersistentIdReceived"/> handler or any other
    /// notification event (same thread as the harvest), or after <see cref="Disconnect"/>.</para>
    /// </remarks>
    IReadOnlyCollection<string> PersistentIds { get; }

    /// <summary>Raised when the client begins connecting to the FCM server.</summary>
    event EventHandler? Connecting;

    /// <summary>Raised when the client has successfully connected and logged in.</summary>
    event EventHandler? Connected;

    /// <summary>Raised when a raw FCM notification JSON string is received.</summary>
    event EventHandler<string>? NotificationReceived;

    /// <summary>Raised when the client begins disconnecting.</summary>
    event EventHandler? Disconnecting;

    /// <summary>Raised when the client has fully disconnected.</summary>
    event EventHandler? Disconnected;

    /// <summary>Raised when the server sends a close tag.</summary>
    event EventHandler? SocketClosed;

    /// <summary>Raised when an unhandled exception occurs on the receive loop.</summary>
    event EventHandler<Exception>? ErrorOccurred;

    /// <summary>Raised once per newly-harvested FCM <c>persistentId</c>, after it is tracked.
    /// Subscribe to persist ids incrementally and minimise the cross-session redelivery window.</summary>
    event EventHandler<string>? PersistentIdReceived;

    /// <summary>Connects to the FCM MCS endpoint and begins receiving notifications. On failure,
    /// <c>ErrorOccurred</c> is raised and the exception is rethrown. Instances are single-connection:
    /// after <see cref="Disconnect"/> or disposal, create a new instance to reconnect.</summary>
    /// <param name="cancellationToken">A token to cancel the connection attempt.</param>
    /// <exception cref="InvalidOperationException">Thrown when the socket is already connected or was closed.</exception>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Cancels the receive loop and releases socket resources.</summary>
    void Disconnect();
}
