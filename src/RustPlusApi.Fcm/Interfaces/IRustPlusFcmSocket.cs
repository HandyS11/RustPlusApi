namespace RustPlusApi.Fcm.Interfaces;

/// <summary>Defines the connection lifecycle and notification events for a low-level FCM MCS socket.
/// Clients are disposable; prefer <see cref="IAsyncDisposable.DisposeAsync"/> so teardown drains background work.</summary>
public interface IRustPlusFcmSocket : IDisposable, IAsyncDisposable
{
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

    /// <summary>Connects to the FCM MCS endpoint and begins receiving notifications. On failure,
    /// <c>ErrorOccurred</c> is raised and the exception is rethrown. Instances are single-connection:
    /// after <see cref="Disconnect"/> or disposal, create a new instance to reconnect.</summary>
    /// <param name="cancellationToken">A token to cancel the connection attempt.</param>
    /// <exception cref="InvalidOperationException">Thrown when the socket is already connected or was closed.</exception>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Cancels the receive loop and releases socket resources.</summary>
    void Disconnect();
}
