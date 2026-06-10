using RustPlusContracts;

namespace RustPlusApi.Interfaces;

/// <summary>Low-level WebSocket contract — connection lifecycle and raw message events.
/// Clients are disposable; prefer <see cref="IAsyncDisposable.DisposeAsync"/> so teardown drains background work.</summary>
public interface IRustPlusSocket : IDisposable, IAsyncDisposable
{
    /// <summary>Raised just before the WebSocket connection attempt begins.</summary>
    event EventHandler? Connecting;

    /// <summary>Raised when the WebSocket connection is established.</summary>
    event EventHandler? Connected;

    /// <summary>Raised when a request is about to be sent to the server.</summary>
    event EventHandler? SendingRequest;

    /// <summary>Raised after a request has been serialized and sent.</summary>
    event EventHandler<AppRequest>? RequestSent;

    /// <summary>Raised for every inbound <c>AppMessage</c>, before routing.</summary>
    event EventHandler<AppMessage>? MessageReceived;

    /// <summary>Raised for inbound messages that are push notifications (no matching pending request).</summary>
    event EventHandler<AppMessage>? NotificationReceived;

    /// <summary>Raised for inbound messages that are responses to a pending request.</summary>
    event EventHandler<AppMessage>? ResponseReceived;

    /// <summary>Raised just before the WebSocket connection is closed.</summary>
    event EventHandler? Disconnecting;

    /// <summary>Raised after the WebSocket connection has been closed.</summary>
    event EventHandler? Disconnected;

    /// <summary>Raised when an unhandled exception occurs on the receive loop.</summary>
    event EventHandler<Exception>? ErrorOccurred;

    /// <summary>Opens the WebSocket connection to the server. On failure, <c>ErrorOccurred</c> is raised
    /// and the exception is rethrown. May be called again after a disconnect to reconnect.</summary>
    /// <param name="cancellationToken">A token to cancel the connection attempt.</param>
    /// <exception cref="ObjectDisposedException">Thrown when the client has been disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the client is already connected.</exception>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes the WebSocket connection.</summary>
    /// <param name="forceClose">When <see langword="true"/>, aborts the connection immediately instead of sending a close handshake.</param>
    Task DisconnectAsync(bool forceClose = false);

    /// <summary>Gets a value indicating whether the WebSocket connection is currently open.</summary>
    bool IsConnected { get; }
}
