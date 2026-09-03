using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Registration;

namespace RustPlusApi.CredentialsWeb.Sessions;

/// <summary>One visitor's flow: its state, its secrets and their lifetimes. Everything here is
/// in-memory only and dies with <see cref="Dispose"/> or the process — nothing is ever persisted.</summary>
/// <param name="sessionId">The handle the browser uses for the event stream and follow-up calls.</param>
/// <param name="returnToken">Single-use token embedded in the Facepunch <c>returnUrl</c> path.</param>
/// <param name="clientIp">The caller's address, for per-IP accounting.</param>
/// <param name="expiresAt">When this session becomes sweepable.</param>
internal sealed class Session(string sessionId, string returnToken, string clientIp, DateTimeOffset expiresAt)
    : IDisposable
{
    private readonly Lock _gate = new();
    private bool _disposed;

    /// <summary>Background work started for this session, kept so it is observed rather than fire-and-forget.</summary>
    internal Task BackgroundWork { get; set; } = Task.CompletedTask;

    /// <summary>The caller's address.</summary>
    internal string ClientIp { get; } = clientIp;

    /// <summary>Credentials from steps 1-3, once acquired.</summary>
    internal Credentials? Credentials { get; private set; }

    /// <summary>This session's event stream.</summary>
    internal SessionEventStream Events { get; } = new();

    /// <summary>When this session becomes sweepable.</summary>
    internal DateTimeOffset ExpiresAt { get; private set; } = expiresAt;

    /// <summary>Cancelled on disposal, so any in-flight upstream work stops with the session.</summary>
    internal CancellationTokenSource Lifetime { get; } = new();

    /// <summary>The pairing, once a push arrives.</summary>
    internal ServerPairing? Pairing { get; private set; }

    /// <summary>Single-use token embedded in the Facepunch <c>returnUrl</c> path.</summary>
    internal string ReturnToken { get; } = returnToken;

    /// <summary>The handle the browser uses. Never appears in a <c>returnUrl</c>.</summary>
    internal string SessionId { get; } = sessionId;

    /// <summary>The Steam64 from the callback. Not a secret; kept for display.</summary>
    internal ulong SteamId { get; private set; }

    /// <summary>The Steam auth token. Dropped the moment step 5 succeeds.</summary>
    internal string? SteamToken { get; private set; }

    /// <summary>Where the visitor is in the flow.</summary>
    internal SessionState State { get; private set; } = SessionState.Created;

    /// <summary>Cancels in-flight work, ends the event stream and drops every secret. Idempotent.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            SteamToken = null;
            Credentials = null;
            Pairing = null;
        }

        // Outside the lock: cancellation callbacks may re-enter session code.
        Lifetime.Cancel();
        Events.Complete();
        Lifetime.Dispose();
    }

    /// <summary>Moves to <paramref name="state"/>, resets the expiry and publishes a <c>step</c> event.</summary>
    /// <param name="state">The new state.</param>
    /// <param name="newExpiry">The new expiry instant.</param>
    internal void Advance(SessionState state, DateTimeOffset newExpiry)
    {
        lock (_gate)
        {
            State = state;
            ExpiresAt = newExpiry;
        }

        Events.Publish(new SessionEvent("step", new StepPayload(state.ToString())));
    }

    /// <summary>Drops the Steam auth token once it has no further use.</summary>
    internal void ClearSteamToken()
    {
        lock (_gate)
        {
            SteamToken = null;
        }
    }

    /// <summary>True once <paramref name="now"/> has reached the expiry.</summary>
    /// <param name="now">The current instant, from the ambient <see cref="TimeProvider"/>.</param>
    internal bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>Stores the credentials from steps 1-3.</summary>
    /// <param name="credentials">The acquired credentials.</param>
    internal void SetCredentials(Credentials credentials)
    {
        lock (_gate)
        {
            Credentials = credentials;
        }
    }

    /// <summary>Stores the pairing from step 6.</summary>
    /// <param name="pairing">The pairing that arrived.</param>
    internal void SetPairing(ServerPairing pairing)
    {
        lock (_gate)
        {
            Pairing = pairing;
        }
    }

    /// <summary>Stores the Steam identity captured from the Facepunch callback.</summary>
    /// <param name="login">The parsed callback result.</param>
    internal void SetSteamLogin(SteamLoginResult login)
    {
        lock (_gate)
        {
            SteamId = login.SteamId;
            SteamToken = login.Token;
        }
    }
}
