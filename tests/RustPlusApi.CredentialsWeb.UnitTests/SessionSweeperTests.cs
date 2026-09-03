using Microsoft.Extensions.Time.Testing;
using RustPlusApi.CredentialsWeb;
using RustPlusApi.CredentialsWeb.Sessions;
using Xunit;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class SessionSweeperTests
{
    [Fact]
    public async Task ExecuteAsync_RemovesExpiredSessionsOnTick()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));
        var options = new AppOptions { PublicBaseUrl = "https://creds.example.org" };
        using var store = new SessionStore(options, time);
        store.TryCreate("203.0.113.7", out var session, out _);

        using var sweeper = new SessionSweeper(store, time);
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
        store.TryCreate("203.0.113.7", out var session, out _);

        using var sweeper = new SessionSweeper(store, time);
        await sweeper.StartAsync(CancellationToken.None);

        // Advance past one sweep interval to ensure the sweeper demonstrably ran, but not past TTL
        time.Advance(TimeSpan.FromSeconds(31));
        await Task.Delay(100);

        await sweeper.StopAsync(CancellationToken.None);
        Assert.True(store.TryGet(session!.SessionId, out _));
    }
}
