using RustPlusApi.Fcm.Registration.Steps.Browser;
using RustPlusApi.Fcm.Registration.Steps.Cdp;

namespace RustPlusApi.Fcm.Registration.UnitTests.TestHelpers;

/// <summary>
/// An in-memory <see cref="IBrowserLauncher"/>: records the launch, then either fails with <see cref="Failure"/> or
/// returns a <see cref="FakeBrowserSession"/> over <see cref="Transport"/>.
/// </summary>
internal sealed class FakeBrowserLauncher : IBrowserLauncher
{
    /// <summary>The DevTools connection the launched browser exposes.</summary>
    public FakeCdpTransport Transport { get; } = new();

    /// <summary>When set, <see cref="LaunchAsync"/> fails with this instead of returning a session.</summary>
    public Exception? Failure { get; set; }

    /// <summary>When set, the returned session's <see cref="FakeBrowserSession.DisposeAsync"/> throws this.</summary>
    public Exception? SessionDisposeFailure { get; set; }

    /// <summary>Invoked at the start of every launch, before it fails or succeeds.</summary>
    public Action? OnLaunch { get; set; }

    /// <summary>How many launches were attempted.</summary>
    public int Calls { get; private set; }

    /// <summary>The browser of the last launch.</summary>
    public BrowserInfo? Browser { get; private set; }

    /// <summary>The debugging port of the last launch.</summary>
    public int Port { get; private set; }

    /// <summary>The profile directory of the last launch.</summary>
    public string? ProfileDirectory { get; private set; }

    /// <summary>The session the last successful launch returned.</summary>
    public FakeBrowserSession? Session { get; private set; }

    /// <inheritdoc/>
    public Task<IBrowserSession> LaunchAsync(BrowserInfo browser,
        int port,
        string profileDirectory,
        CancellationToken cancellationToken)
    {
        Calls++;
        Browser = browser;
        Port = port;
        ProfileDirectory = profileDirectory;
        OnLaunch?.Invoke();
        if (Failure is not null)
        {
            return Task.FromException<IBrowserSession>(Failure);
        }

        Session = new FakeBrowserSession(Transport, profileDirectory, SessionDisposeFailure);
        return Task.FromResult<IBrowserSession>(Session);
    }
}

/// <summary>A launched fake browser: records how and when it was shut down.</summary>
/// <param name="transport">The DevTools connection.</param>
/// <param name="profileDirectory">The profile the browser was started with.</param>
/// <param name="disposeFailure">Thrown by <see cref="DisposeAsync"/> when set.</param>
internal sealed class FakeBrowserSession(FakeCdpTransport transport, string profileDirectory, Exception? disposeFailure)
    : IBrowserSession
{
    /// <summary>Whether <see cref="DisposeAsync"/> has been called.</summary>
    public bool Disposed { get; private set; }

    /// <summary>Whether the DevTools connection was already disposed when the browser was shut down.</summary>
    public bool TransportDisposedFirst { get; private set; }

    /// <summary>Whether the profile directory still existed when the browser was shut down.</summary>
    public bool ProfileExistedAtDispose { get; private set; }

    /// <inheritdoc/>
    public ICdpTransport Transport => transport;

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Disposed = true;
        TransportDisposedFirst = transport.Disposed;
        ProfileExistedAtDispose = Directory.Exists(profileDirectory);
        if (disposeFailure is not null)
        {
            throw disposeFailure;
        }

        return default;
    }
}
