using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RustPlusApi.CredentialsWeb;
using RustPlusApi.CredentialsWeb.Sessions;
using Xunit;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class SessionSweeperTests
{
    private static ILogger<SessionSweeper> NullLogger => NullLoggerFactory.Instance.CreateLogger<SessionSweeper>();

    [Fact]
    public async Task ExecuteAsync_RemovesExpiredSessionsOnTick()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));
        var options = new AppOptions
        {
            PublicBaseUrl = "https://creds.example.org"
        };
        using var store = new SessionStore(options, time);
        store.TryCreate("203.0.113.7", isLocal: true, out var session, out _);

        using var sweeper = new SessionSweeper(store, time, NullLogger);
        await sweeper.StartAsync(CancellationToken.None);

        // Advance past TTL. Tolerate the startup race: the sweeper may not have created its timer yet,
        // so repeated advances ensure it fires after being created.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (store.TryGet(session!.SessionId, out _))
        {
            timeout.Token.ThrowIfCancellationRequested();
            time.Advance(TimeSpan.FromSeconds(31)); // Advance past sweep interval to ensure timer fires
            await Task.Delay(10, timeout.Token);
        }

        await sweeper.StopAsync(CancellationToken.None);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task ExecuteAsync_LeavesLiveSessionsAlone()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));
        var options = new AppOptions
        {
            PublicBaseUrl = "https://creds.example.org"
        };
        using var store = new SessionStore(options, time);

        // Create a canary session from one IP at time 0, expires at 5 minutes
        store.TryCreate("203.0.113.7", isLocal: true, out var canary, out _);

        // Advance time by 1 minute
        time.Advance(TimeSpan.FromMinutes(1));

        // Create a live session from a different IP, expires at 1+5=6 minutes
        store.TryCreate("203.0.113.8", isLocal: true, out var liveSession, out _);

        using var sweeper = new SessionSweeper(store, time, NullLogger);
        await sweeper.StartAsync(CancellationToken.None);

        // Advance past canary's TTL (5 minutes) repeatedly until it is removed,
        // proving the sweeper actually ran. The live session (expires at 6 minutes) should still be present.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (store.TryGet(canary!.SessionId, out _))
        {
            timeout.Token.ThrowIfCancellationRequested();
            time.Advance(TimeSpan.FromSeconds(31)); // Advance past sweep interval to ensure timer fires
            await Task.Delay(10, timeout.Token);
        }

        await sweeper.StopAsync(CancellationToken.None);
        Assert.True(store.TryGet(liveSession!.SessionId, out _), "Live session should survive sweep");
    }

    [Fact]
    public async Task ExecuteAsync_RemovesSessionWithThrowingCallback_AndKeepsSweepingOthers()
    {
        // A session whose cancellation callback throws (Cancel() rethrows callback exceptions) must
        // no longer disrupt sweeping at all: Session.Dispose() now swallows that exception and still
        // completes its own cleanup, and the sweeper must still go on to reap sessions on other IPs.
        // This models the real scenario where pairing tasks register callbacks that may throw.
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));
        var options = new AppOptions
        {
            PublicBaseUrl = "https://creds.example.org"
        };
        using var store = new SessionStore(options, time);

        // Expires at 5 minutes.
        store.TryCreate("203.0.113.7", isLocal: true, out var throwingSession, out _);
        throwingSession!.Lifetime.Token.Register(() =>
            throw new InvalidOperationException("Simulated callback failure"));

        // Advance time by 1 minute so the canary's expiry is staggered.
        time.Advance(TimeSpan.FromMinutes(1));

        // Create a canary session from a different IP. Expires at 1+5=6 minutes.
        // Staggering ensures the canary can only be removed on a tick *after* the throwing session.
        store.TryCreate("203.0.113.8", isLocal: true, out var canary, out _);

        using var sweeper = new SessionSweeper(store, time, NullLogger);
        await sweeper.StartAsync(CancellationToken.None);

        // Advance past the canary's TTL. If the sweeper had died (or SweepExpired had aborted the
        // rest of its own loop) on the throwing session, the canary would never be removed.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (store.TryGet(canary!.SessionId, out _))
        {
            timeout.Token.ThrowIfCancellationRequested();
            time.Advance(TimeSpan.FromSeconds(31)); // Advance past sweep interval to ensure timer fires
            await Task.Delay(10, timeout.Token);
        }

        await sweeper.StopAsync(CancellationToken.None);

        Assert.False(store.TryGet(throwingSession.SessionId, out _),
            "Session with a throwing cancellation callback should still be removed");
        Assert.Equal(0, store.Count);

        // The stronger property Copilot's finding calls for: this wasn't a removal that aborted
        // partway through Dispose() because Cancel() threw. A disposed CancellationTokenSource
        // throws ObjectDisposedException on Token access, which only holds if Session.Dispose()
        // still reached Lifetime.Dispose() despite Cancel() throwing above it. Before the fix, the
        // exception from Cancel() escaped Dispose() before Events.Complete()/Lifetime.Dispose() ran,
        // so this assertion fails against the unfixed code even though the two removal assertions
        // above already pass there (the sweeper's own outer try/catch was already enough to recover
        // on the next tick).
        Assert.Throws<ObjectDisposedException>(() => _ = throwingSession.Lifetime.Token);
    }

    [Fact]
    public async Task ExecuteAsync_LogsAndKeepsTickingWhenASweepThrows()
    {
        // The per-tick try/catch is the only thing standing between one bad sweep and a sweeper that
        // never runs again — and a dead sweeper is silent, so nothing else in the app would notice
        // that sessions had stopped expiring. Fail one sweep outright, then let the next succeed.
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));
        var options = new AppOptions
        {
            PublicBaseUrl = "https://creds.example.org"
        };

        // Only the store's clock is made to fail: the sweeper keeps a working one so its timer
        // still ticks while SweepExpired throws.
        var storeClock = new FailableTimeProvider(time);
        using var store = new SessionStore(options, storeClock);
        store.TryCreate("203.0.113.7", isLocal: true, out var session, out _);

        var records = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(records));

        using var sweeper = new SessionSweeper(store, time, loggerFactory.CreateLogger<SessionSweeper>());
        await sweeper.StartAsync(CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        storeClock.Failing = true;
        while (!records.Records.Any(r => r.Contains("Sweep failed", StringComparison.Ordinal)))
        {
            timeout.Token.ThrowIfCancellationRequested();
            time.Advance(TimeSpan.FromSeconds(31));
            await Task.Delay(10, timeout.Token);
        }

        // Recovery: with the clock working again the sweeper must still be running its loop.
        storeClock.Failing = false;
        time.Advance(TimeSpan.FromMinutes(10));
        while (store.TryGet(session!.SessionId, out _))
        {
            timeout.Token.ThrowIfCancellationRequested();
            time.Advance(TimeSpan.FromSeconds(31));
            await Task.Delay(10, timeout.Token);
        }

        await sweeper.StopAsync(CancellationToken.None);
        Assert.Equal(0, store.Count);
    }

    /// <summary>A clock that can be made to throw on demand, so <c>SweepExpired</c> fails without
    /// the test needing a seam inside the store.</summary>
    /// <param name="inner">The clock to delegate to while healthy.</param>
    private sealed class FailableTimeProvider(TimeProvider inner) : TimeProvider
    {
        internal bool Failing { get; set; }

        public override DateTimeOffset GetUtcNow() =>
            Failing
                ? throw new InvalidOperationException("Simulated clock failure")
                : inner.GetUtcNow();
    }
}
