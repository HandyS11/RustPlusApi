using RustPlusApi.Fcm.Registration.Steps.Cdp;

namespace RustPlusApi.Fcm.Registration.Steps.Browser;

/// <summary>A browser started under remote control. Disposing it shuts the browser down: it closes the DevTools
/// connection, waits for the process to exit and kills it if it does not.</summary>
internal interface IBrowserSession : IAsyncDisposable
{
    /// <summary>Gets the DevTools connection to the browser's page. A <see cref="CdpSession"/> built over it disposes
    /// it too, so disposing it twice must be harmless.</summary>
    ICdpTransport Transport { get; }
}

/// <summary>Starts a browser with a debugging port and connects to its page (a test seam).</summary>
internal interface IBrowserLauncher
{
    /// <summary>Starts <paramref name="browser"/> and connects to its first page.</summary>
    /// <param name="browser">The browser to start.</param>
    /// <param name="port">The remote-debugging port to open, on the loopback address.</param>
    /// <param name="profileDirectory">The throwaway user-data directory.</param>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>The running browser; the caller disposes it.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>Any other exception means the browser could not be started or reached; the process is gone by then.</remarks>
    Task<IBrowserSession> LaunchAsync(BrowserInfo browser,
        int port,
        string profileDirectory,
        CancellationToken cancellationToken);
}
