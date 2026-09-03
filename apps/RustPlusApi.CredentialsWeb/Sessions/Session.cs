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
    private bool _claimedForEviction;
    private bool _disposed;

    private DateTimeOffset _expiresAt = expiresAt;

    /// <summary>Background work started for this session, kept so it is observed rather than fire-and-forget.</summary>
    internal Task BackgroundWork { get; set; } = Task.CompletedTask;

    /// <summary>The caller's address.</summary>
    internal string ClientIp { get; } = clientIp;

    /// <summary>Credentials from steps 1-3, once acquired.</summary>
    internal Credentials? Credentials { get; private set; }

    /// <summary>This session's event stream.</summary>
    internal SessionEventStream Events { get; } = new();

    /// <summary>When this session becomes sweepable. The getter takes <see cref="_gate"/>: the
    /// sweeper reads this concurrently with <see cref="Advance"/>'s writes on the flow's own thread,
    /// and <see cref="DateTimeOffset"/> is not guaranteed to read or write atomically.</summary>
    internal DateTimeOffset ExpiresAt
    {
        get
        {
            lock (_gate)
            {
                return _expiresAt;
            }
        }
    }

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

    /// <summary>The Steam auth token. Dropped on success, failure and cancellation alike — never
    /// retained once step 5 has run to any conclusion.</summary>
    internal string? SteamToken { get; private set; }

    /// <summary>Where the visitor is in the flow.</summary>
    internal SessionState State { get; private set; } = SessionState.Created;

    /// <summary>Cancels in-flight work, ends the event stream and drops every secret. Idempotent
    /// and never throws: <see cref="CancellationTokenSource.Cancel()"/> rethrows any exception a
    /// registered callback raises, and letting that escape here would abort cleanup for whichever
    /// caller is disposing this session — the sweeper mid-sweep, or <see cref="SessionStore.TryCreate"/>
    /// evicting a batch — and skip <see cref="Events"/>' completion and <see cref="Lifetime"/>'s own
    /// disposal for good measure, leaking both. The callback exception is swallowed instead, and the
    /// rest of cleanup runs in a <c>finally</c> regardless.</summary>
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
        try
        {
            Lifetime.Cancel();
        }
#pragma warning disable CA1031, RCS1075 // Deliberate: Dispose() must never throw; see the remarks above.
        catch (Exception)
#pragma warning restore CA1031, RCS1075
        {
            // Swallowed by design (see remarks). Not logged: this indicates a bug in whatever
            // registered the throwing callback on Lifetime.Token, not in the session itself, and
            // Session carries no logger today — it is constructed directly by SessionStore rather
            // than through DI, so threading one through every construction site (including the
            // many direct `new Session(...)` calls in tests) for a single defensive catch was
            // judged not worth the added surface. A misbehaving callback is caught by
            // SessionSweeperTests instead.
        }
        finally
        {
            Events.Complete();
            Lifetime.Dispose();
        }
    }

    /// <summary>Moves to <paramref name="state"/>, resets the expiry and publishes a <c>step</c> event.
    /// A no-op once the session is disposed or <see cref="TryClaimForEviction"/> has claimed it.</summary>
    /// <param name="state">The new state.</param>
    /// <param name="newExpiry">The new expiry instant.</param>
    internal void Advance(SessionState state, DateTimeOffset newExpiry)
    {
        lock (_gate)
        {
            if (_disposed || _claimedForEviction)
            {
                return;
            }

            State = state;
            _expiresAt = newExpiry;
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

    /// <summary>Stores the credentials from steps 1-3. A no-op once the session is disposed or
    /// <see cref="TryClaimForEviction"/> has claimed it.</summary>
    /// <param name="credentials">The acquired credentials.</param>
    internal void SetCredentials(Credentials credentials)
    {
        lock (_gate)
        {
            if (_disposed || _claimedForEviction)
            {
                return;
            }

            Credentials = credentials;
        }
    }

    /// <summary>Stores the pairing from step 6. A no-op once the session is disposed or
    /// <see cref="TryClaimForEviction"/> has claimed it.</summary>
    /// <param name="pairing">The pairing that arrived.</param>
    internal void SetPairing(ServerPairing pairing)
    {
        lock (_gate)
        {
            if (_disposed || _claimedForEviction)
            {
                return;
            }

            Pairing = pairing;
        }
    }

    /// <summary>Stores the Steam identity captured from the Facepunch callback. A no-op once the
    /// session is disposed or <see cref="TryClaimForEviction"/> has claimed it.</summary>
    /// <param name="login">The parsed callback result.</param>
    internal void SetSteamLogin(SteamLoginResult login)
    {
        lock (_gate)
        {
            if (_disposed || _claimedForEviction)
            {
                return;
            }

            SteamId = login.SteamId;
            SteamToken = login.Token;
        }
    }

    /// <summary><para>Atomically decides, with respect to any concurrent <see cref="Advance"/> call,
    /// whether <see cref="SessionStore.TryCreate"/> may evict this session. Only a session still in
    /// <see cref="SessionState.Created"/> (it never touched upstream) or terminally
    /// <see cref="SessionState.Failed"/> is evictable; anything else has resumable upstream work and
    /// the caller must refuse instead of evicting.</para>
    ///
    /// <para>This runs under the same <see cref="_gate"/> that <see cref="Advance"/> writes
    /// <see cref="State"/> under, so a racing <see cref="Advance"/> call can never interleave with
    /// the decision: it either commits first — this call then observes the new state and returns
    /// <see langword="false"/>, correctly refusing rather than evicting mid-flight work — or it
    /// loses the race, in which case this call marks the session so that <see cref="Advance"/> (and
    /// every other setter) becomes a no-op instead of resurrecting a session that is about to be
    /// disposed. Deliberately a separate flag from <see cref="_disposed"/>: unlike disposal, a claim
    /// here does not itself run any cleanup — <see cref="Dispose"/> still has to.</para>
    ///
    /// <para>A session that is already disposed returns <see langword="true"/> too: something else
    /// (the sweeper, a concurrent eviction) already claimed and is disposing it, so there is nothing
    /// left here to protect, and refusing would incorrectly block a new session over one that is
    /// already gone.</para></summary>
    internal bool TryClaimForEviction()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return true;
            }

            if (State != SessionState.Created && State != SessionState.Failed)
            {
                return false;
            }

            _claimedForEviction = true;
            return true;
        }
    }
}
