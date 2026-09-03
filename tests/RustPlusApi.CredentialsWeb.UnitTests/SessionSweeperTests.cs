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
        var options = new AppOptions { PublicBaseUrl = "https://creds.example.org" };
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
        var options = new AppOptions { PublicBaseUrl = "https://creds.example.org" };
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

}
