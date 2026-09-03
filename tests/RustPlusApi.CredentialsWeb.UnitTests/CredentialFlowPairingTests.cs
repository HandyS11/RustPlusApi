using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RustPlusApi.CredentialsWeb;
using RustPlusApi.CredentialsWeb.Flow;
using RustPlusApi.CredentialsWeb.Sessions;
using RustPlusApi.Fcm.Registration;
using Xunit;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class CredentialFlowPairingTests
{
    private static readonly DateTimeOffset Origin = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static async Task<(CredentialFlow Flow, SessionStore Store, FakeRegistrationSteps Steps, Session Session)>
        ReadySessionAsync(Action<AppOptions>? configure = null)
    {
        var time = new FakeTimeProvider(Origin);
        var options = new AppOptions { PublicBaseUrl = "https://creds.example.org" };
        configure?.Invoke(options);
        var store = new SessionStore(options, time);
        var steps = new FakeRegistrationSteps();
        var flow = new CredentialFlow(steps, store, options, time, NullLogger<CredentialFlow>.Instance);

        store.TryCreate("203.0.113.7", out var session, out _);
        await flow.CompleteRegistrationAsync(
            session!,
            new SteamLoginResult(76561198249527954, "steam-token"),
            CancellationToken.None);

        steps.Calls.Clear();
        return (flow, store, steps, session!);
    }

    /// <summary>Drains the buffered events; the short window is what ends the open-ended stream.</summary>
    /// <param name="session">The session whose event stream is drained.</param>
    private static async Task<List<SessionEvent>> EventsOfAsync(Session session)
    {
        var received = new List<SessionEvent>();
        using var window = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        try
        {
            await foreach (var item in session.Events.SubscribeAsync(window.Token))
            {
                received.Add(item);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected: the window closed.
        }

        return received;
    }

    [Fact]
    public async Task WaitForPairingAsync_ReachesPairedAndPublishesTheFourValues()
    {
        var (flow, store, _, session) = await ReadySessionAsync();
        using var _s = store;
        store.TryAcquirePairingSlot();

        await flow.WaitForPairingAsync(session, CancellationToken.None);

        Assert.Equal(SessionState.Paired, session.State);

        var events = await EventsOfAsync(session);
        var payload = Assert.IsType<PairedPayload>(Assert.Single(events, e => e.Type == "paired").Data);

        Assert.Equal("10.0.0.1", payload.Ip);
        Assert.Equal(28082, payload.Port);
        Assert.Equal("76561198249527954", payload.PlayerId);
        Assert.Equal(987654321, payload.PlayerToken);
        Assert.Equal("Test Server", payload.Name);
    }

    [Fact]
    public async Task WaitForPairingAsync_ReleasesThePairingSlotOnSuccess()
    {
        var (flow, store, _, session) = await ReadySessionAsync();
        using var _s = store;
        store.TryAcquirePairingSlot();

        await flow.WaitForPairingAsync(session, CancellationToken.None);

        Assert.Equal(0, store.ActivePairings);
    }

    [Fact]
    public async Task WaitForPairingAsync_MovesToAwaitingPairingWithThePairingTtl()
    {
        var (flow, store, steps, session) = await ReadySessionAsync(o => o.PairingTtl = TimeSpan.FromMinutes(10));
        using var _s = store;
        steps.PairingWaitsForGate = true;
        store.TryAcquirePairingSlot();

        // CA2025: the task is deliberately observed mid-flight (polling for AwaitingPairing) before
        // being awaited below, which happens well before `_s`/`store` are disposed at method end.
#pragma warning disable CA2025
        var pending = flow.WaitForPairingAsync(session, CancellationToken.None);
#pragma warning restore CA2025
        while (session.State != SessionState.AwaitingPairing)
        {
            await Task.Delay(10);
        }

        Assert.Equal(Origin.AddMinutes(10), session.ExpiresAt);

        steps.PairingGate.SetResult();
        await pending;
    }

    [Fact]
    public async Task WaitForPairingAsync_ReleasesTheSlotWhenTheWaitFails()
    {
        var (flow, store, steps, session) = await ReadySessionAsync();
        using var _s = store;
        steps.PairingFailure = new InvalidOperationException("socket died");
        store.TryAcquirePairingSlot();

        await flow.WaitForPairingAsync(session, CancellationToken.None);

        Assert.Equal(0, store.ActivePairings);
        Assert.Equal(SessionState.Failed, session.State);

        var events = await EventsOfAsync(session);
        var error = Assert.IsType<ErrorPayload>(Assert.Single(events, e => e.Type == "error").Data);
        Assert.DoesNotContain("socket died", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitForPairingAsync_ReturnsToReadyAndReleasesTheSlotWhenCancelled()
    {
        var (flow, store, steps, session) = await ReadySessionAsync();
        using var _s = store;
        steps.PairingWaitsForGate = true;
        store.TryAcquirePairingSlot();

        using var cancellation = new CancellationTokenSource();

        // CA2025: awaited below, well before `cancellation` or `_s`/`store` are disposed.
#pragma warning disable CA2025
        var pending = flow.WaitForPairingAsync(session, cancellation.Token);
#pragma warning restore CA2025
        while (session.State != SessionState.AwaitingPairing)
        {
            await Task.Delay(10);
        }

        await cancellation.CancelAsync();
        await pending;

        Assert.Equal(0, store.ActivePairings);
        Assert.Equal(SessionState.Ready, session.State);

        var events = await EventsOfAsync(session);
        Assert.Contains(events, e => e.Type == "expired");
    }

    [Fact]
    public async Task WaitForPairingAsync_CanBeRetriedAfterATimeout()
    {
        var (flow, store, steps, session) = await ReadySessionAsync();
        using var _s = store;
        steps.PairingWaitsForGate = true;
        store.TryAcquirePairingSlot();

        using var cancellation = new CancellationTokenSource();

        // CA2025: awaited below, well before `cancellation` or `_s`/`store` are disposed.
#pragma warning disable CA2025
        var pending = flow.WaitForPairingAsync(session, cancellation.Token);
#pragma warning restore CA2025
        while (session.State != SessionState.AwaitingPairing)
        {
            await Task.Delay(10);
        }

        await cancellation.CancelAsync();
        await pending;

        // The second attempt must be accepted: this is what the "retry without redoing the Steam
        // login" promise actually depends on.
        steps.PairingWaitsForGate = false;
        Assert.True(store.TryAcquirePairingSlot());
        await flow.WaitForPairingAsync(session, CancellationToken.None);

        Assert.Equal(SessionState.Paired, session.State);
    }

    [Fact]
    public async Task WaitForPairingAsync_RefusesWhenTheSessionIsNotReady()
    {
        var (flow, store, steps, session) = await ReadySessionAsync();
        using var _s = store;
        session.Advance(SessionState.Failed, Origin.AddMinutes(15));
        store.TryAcquirePairingSlot();

        await flow.WaitForPairingAsync(session, CancellationToken.None);

        Assert.Empty(steps.Calls);
        Assert.Equal(0, store.ActivePairings);
    }
}
