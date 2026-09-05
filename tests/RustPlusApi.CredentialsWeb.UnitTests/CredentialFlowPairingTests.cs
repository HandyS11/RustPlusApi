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
        var options = new AppOptions
        {
            PublicBaseUrl = "https://creds.example.org"
        };
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

    /// <summary>Same as <see cref="ReadySessionAsync"/> but also hands back the clock, for tests that
    /// need to drive the pairing wait's own TimeProvider-based deadline instead of a standalone
    /// <see cref="CancellationTokenSource"/> that production never constructs.</summary>
    /// <param name="configure">Optional options tweak.</param>
    private static async Task<(CredentialFlow Flow, SessionStore Store, FakeRegistrationSteps Steps,
        Session Session, FakeTimeProvider Time)> ReadySessionWithClockAsync(Action<AppOptions>? configure = null)
    {
        var time = new FakeTimeProvider(Origin);
        var options = new AppOptions
        {
            PublicBaseUrl = "https://creds.example.org"
        };
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
        return (flow, store, steps, session!, time);
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
    public async Task WaitForPairingAsync_MovesToAwaitingPairingWithTheSessionTtlNotThePairingTtl()
    {
        // PairingTtl and SessionTtl are deliberately different: if AwaitingPairing's expiry ever
        // regresses to PairingTtl, the sweeper would reap the session the moment the pairing wait's
        // own (much shorter) deadline elapses — see
        // WaitForPairingAsync_SurvivesASweepPastThePairingTtlButBeforeTheSessionTtl below for that
        // regression caught end to end.
        var (flow, store, steps, session) = await ReadySessionAsync(o =>
        {
            o.PairingTtl = TimeSpan.FromMinutes(10);
            o.SessionTtl = TimeSpan.FromMinutes(20);
        });
        using var _s = store;
        steps.PairingWaitsForGate = true;
        store.TryAcquirePairingSlot();

        // CA2025: the task is deliberately observed mid-flight (polling for AwaitingPairing) before
        // being awaited below, which happens well before `_s`/`store` are disposed at method end.
#pragma warning disable CA2025
        var pending = flow.WaitForPairingAsync(session, CancellationToken.None);
#pragma warning restore CA2025
        // Bounded: a regression that never reaches AwaitingPairing must fail as an assertion,
        // not park a foreground thread and hang the test process.
        using var reachTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (session.State != SessionState.AwaitingPairing)
        {
            Assert.False(reachTimeout.IsCancellationRequested, "The flow never reached AwaitingPairing.");
            await Task.Delay(10);
        }

        Assert.Equal(Origin.AddMinutes(20), session.ExpiresAt);

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
    public async Task WaitForPairingAsync_StaysSilentWhenTheSessionIsDisposed()
    {
        // Production only ever calls this with session.Lifetime.Token (see SessionEndpoints), and
        // the only way that token is ever cancelled is SessionStore.Remove disposing the session.
        // A standalone CancellationTokenSource that production never constructs would exercise a
        // scenario that cannot actually happen, so this drives the real disposal path instead.
        var (flow, store, steps, session) = await ReadySessionAsync();
        using var _s = store;
        steps.PairingWaitsForGate = true;
        store.TryAcquirePairingSlot();

        // CA2025: awaited below, well before `_s`/`store` are disposed at method end.
#pragma warning disable CA2025
        var pending = flow.WaitForPairingAsync(session, session.Lifetime.Token);
#pragma warning restore CA2025
        // Bounded: a regression that never reaches AwaitingPairing must fail as an assertion,
        // not park a foreground thread and hang the test process.
        using var reachTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (session.State != SessionState.AwaitingPairing)
        {
            Assert.False(reachTimeout.IsCancellationRequested, "The flow never reached AwaitingPairing.");
            await Task.Delay(10);
        }

        store.Remove(session.SessionId);
        using var doneTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await pending.WaitAsync(doneTimeout.Token);

        // Disposal, not a genuine pairing timeout: nothing to report, and nobody is listening to the
        // now-completed event stream anyway.
        Assert.Equal(0, store.ActivePairings);
        Assert.False(store.TryGet(session.SessionId, out _));
    }

    [Fact]
    public async Task WaitForPairingAsync_CanBeRetriedAfterATimeout()
    {
        // Drives the pairing wait's own TimeProvider-backed deadline — the mechanism production
        // actually uses to time a pairing wait out — rather than a standalone
        // CancellationTokenSource cancelled by hand, which production never constructs and which
        // exercises the (different) disposal path instead of a genuine timeout.
        var (flow, store, steps, session, time) =
            await ReadySessionWithClockAsync(o => o.PairingTtl = TimeSpan.FromMinutes(10));
        using var _s = store;
        steps.PairingWaitsForGate = true;
        store.TryAcquirePairingSlot();

        // CA2025: awaited below, well before `_s`/`store` are disposed at method end.
#pragma warning disable CA2025
        var pending = flow.WaitForPairingAsync(session, session.Lifetime.Token);
#pragma warning restore CA2025
        // Bounded: a regression that never reaches AwaitingPairing must fail as an assertion,
        // not park a foreground thread and hang the test process.
        using var reachTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (session.State != SessionState.AwaitingPairing)
        {
            Assert.False(reachTimeout.IsCancellationRequested, "The flow never reached AwaitingPairing.");
            await Task.Delay(10);
        }

        time.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));

        // Bounded: a regression that never times the wait back out to Ready must fail as an
        // assertion, not hang the test process.
        using var settleTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (session.State != SessionState.Ready)
        {
            Assert.False(settleTimeout.IsCancellationRequested, "The pairing wait never timed out back to Ready.");
            await Task.Delay(10);
        }

        await pending;
        Assert.Equal(0, store.ActivePairings);

        var events = await EventsOfAsync(session);
        Assert.Contains(events, e => e.Type == "expired");

        // The second attempt must be accepted: this is what the "retry without redoing the Steam
        // login" promise actually depends on.
        steps.PairingWaitsForGate = false;
        Assert.True(store.TryAcquirePairingSlot());
        await flow.WaitForPairingAsync(session, session.Lifetime.Token);

        Assert.Equal(SessionState.Paired, session.State);
    }

    [Fact]
    public async Task WaitForPairingAsync_SurvivesASweepPastThePairingTtlButBeforeTheSessionTtl()
    {
        // This is the regression the whole-branch review found: AwaitingPairing used to carry the
        // *pairing* wait's TTL as the *session's* own expiry, so the sweeper reaped (and disposed)
        // the session the moment the pairing wait's own, much shorter, deadline elapsed — long
        // before the session's real TTL. That made the "retry the pairing wait without redoing the
        // Steam login" promise (see the design doc's error-handling table, docs/articles/
        // troubleshooting.md and wwwroot/app.js) impossible: by the time the OperationCanceledException
        // handler ran, the session was already gone from the store.
        var (flow, store, steps, session, time) = await ReadySessionWithClockAsync(o =>
        {
            o.PairingTtl = TimeSpan.FromMinutes(10);
            o.SessionTtl = TimeSpan.FromMinutes(15);
        });
        using var _s = store;
        steps.PairingWaitsForGate = true;
        store.TryAcquirePairingSlot();

        // CA2025: awaited below, well before `_s`/`store` are disposed at method end.
#pragma warning disable CA2025
        var pending = flow.WaitForPairingAsync(session, session.Lifetime.Token);
#pragma warning restore CA2025
        // Bounded: a regression that never reaches AwaitingPairing must fail as an assertion,
        // not park a foreground thread and hang the test process.
        using var reachTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (session.State != SessionState.AwaitingPairing)
        {
            Assert.False(reachTimeout.IsCancellationRequested, "The flow never reached AwaitingPairing.");
            await Task.Delay(10);
        }

        // Past the pairing wait's own 10-minute deadline, but well inside the session's 15-minute
        // TTL: the sweeper must not treat this session as expired just because the pairing wait's
        // own wait timed out.
        time.Advance(TimeSpan.FromMinutes(11));

        Assert.Equal(0, store.SweepExpired());
        Assert.True(store.TryGet(session.SessionId, out _), "The session was reaped by the sweeper.");

        // Resumable: let the (already-fired) internal deadline settle the flow back to Ready, then
        // prove a fresh pairing wait succeeds without redoing the Steam login.
        using var settleTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (session.State == SessionState.AwaitingPairing)
        {
            Assert.False(settleTimeout.IsCancellationRequested, "The pairing wait never settled.");
            await Task.Delay(10);
        }

        await pending;
        Assert.Equal(SessionState.Ready, session.State);

        steps.PairingWaitsForGate = false;
        Assert.True(store.TryAcquirePairingSlot());
        await flow.WaitForPairingAsync(session, session.Lifetime.Token);

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

    [Fact]
    public async Task WaitForPairingAsync_RefusesOnceTheSessionHasBeenDisposed()
    {
        // Disposal drops the credentials but leaves State alone, so a session removed between the
        // endpoint's advisory Ready check and this call still reads Ready here. The claim refuses on
        // the missing credentials instead of opening a socket with nothing to pair against — and
        // reads them under the session's own lock, so they cannot be nulled between check and use.
        var (flow, store, steps, session) = await ReadySessionAsync();
        using var _s = store;
        store.Remove(session.SessionId);
        store.TryAcquirePairingSlot();

        await flow.WaitForPairingAsync(session, CancellationToken.None);

        Assert.Equal(SessionState.Ready, session.State);
        Assert.Empty(steps.Calls);
        Assert.Equal(0, store.ActivePairings);
    }

    [Fact]
    public async Task WaitForPairingAsync_LetsOnlyOneOfTwoConcurrentWaitsClaimTheSession()
    {
        // Two POSTs to /api/sessions/{sessionId}/pairing for one session — two tabs on the same
        // handle, or a retried request — both clear the endpoint's advisory Ready check and both
        // land here. A Ready check followed by a separate Advance let both through, so both opened
        // an MCS socket against the same credentials and whichever finished last overwrote the
        // other's outcome: a timeout dropping an already-Paired session back to Ready with a
        // spurious `expired` event, or a socket error flipping it to Failed.
        // Session.TryClaimForPairing is what now holds the session to a single wait.
        var (flow, store, steps, session) = await ReadySessionAsync();
        using var _s = store;
        steps.PairingWaitsForGate = true;
        Assert.True(store.TryAcquirePairingSlot());
        Assert.True(store.TryAcquirePairingSlot());

        // CA2025: both tasks are awaited below, well before `_s`/`store` are disposed at method end.
#pragma warning disable CA2025
        var winner = flow.WaitForPairingAsync(session, CancellationToken.None);
        var loser = flow.WaitForPairingAsync(session, CancellationToken.None);
#pragma warning restore CA2025

        // The claim runs before the first await, so the second dispatch has already lost by the time
        // it hands its task back: nothing started, and its slot handed straight back.
        await loser;

        Assert.Equal(SessionState.AwaitingPairing, session.State);
        Assert.Equal(1, store.ActivePairings);
        Assert.Single(steps.Calls);

        // The winner still finishes normally: one socket, one outcome, nothing to overwrite it.
        steps.PairingGate.SetResult();
        await winner;

        Assert.Equal(SessionState.Paired, session.State);
        Assert.Equal(0, store.ActivePairings);
    }
}
