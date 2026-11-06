namespace RustPlusApi.Fcm.Registration.Steps.Cdp;

/// <summary>A bidirectional text-message channel to a browser's DevTools endpoint.</summary>
internal interface ICdpTransport : IAsyncDisposable
{
    /// <summary>Sends one complete text message.</summary>
    /// <param name="message">The JSON message.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    Task SendAsync(string message, CancellationToken cancellationToken);

    /// <summary>Receives the next complete text message.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The message, or <see langword="null"/> when the connection is closed.</returns>
    Task<string?> ReceiveAsync(CancellationToken cancellationToken);
}
