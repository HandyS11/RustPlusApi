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
        store.TryCreate("203.0.113.7", out var session, out _);

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
        store.TryCreate("203.0.113.7", out var canary, out _);

        // Advance time by 1 minute
        time.Advance(TimeSpan.FromMinutes(1));

        // Create a live session from a different IP, expires at 1+5=6 minutes
        store.TryCreate("203.0.113.8", out var liveSession, out _);

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
    public async Task ExecuteAsync_ContinuesAfterSweepThrows()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));
        var options = new AppOptions
        {
            PublicBaseUrl = "https://creds.example.org"
        };
        using var store = new SessionStore(options, time);

        // Create a session that will throw when disposed (via Lifetime.Token callback).
        // This models the real scenario where pairing tasks register callbacks that may throw.
        // Expires at 5 minutes.
        store.TryCreate("203.0.113.7", out var throwingSession, out _);
        throwingSession!.Lifetime.Token.Register(() =>
            throw new InvalidOperationException("Simulated callback failure"));

        // Advance time by 1 minute so the canary's expiry is staggered.
        time.Advance(TimeSpan.FromMinutes(1));

        // Create a canary session from a different IP. Expires at 1+5=6 minutes.
        // Staggering ensures the canary can only be removed on a tick *after* the throwing session.
        store.TryCreate("203.0.113.8", out var canary, out _);

        using var sweeper = new SessionSweeper(store, time, NullLogger);
        await sweeper.StartAsync(CancellationToken.None);

        // Advance past the canary's TTL. The tick that removes the throwing session will fail with exception,
        // but the service should log and continue. A later tick must remove the canary.
        // If the service had died on the first exception, the canary would never be removed.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (store.TryGet(canary!.SessionId, out _))
        {
            timeout.Token.ThrowIfCancellationRequested();
            time.Advance(TimeSpan.FromSeconds(31)); // Advance past sweep interval to ensure timer fires
            await Task.Delay(10, timeout.Token);
        }

        await sweeper.StopAsync(CancellationToken.None);

        // Canary removal proves the service survived the throwing session's removal.
        Assert.False(store.TryGet(canary.SessionId, out _),
            "Canary should be removed (proves sweeper survived exception)");
        Assert.Equal(0, store.Count);
    }
}
