using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RustPlusApi.CredentialsWeb;
using RustPlusApi.CredentialsWeb.Flow;
using RustPlusApi.CredentialsWeb.Sessions;
using RustPlusApi.Fcm.Registration;
using Xunit;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class CredentialFlowTests
{
    private static readonly DateTimeOffset Origin = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static SteamLoginResult Login() =>
        new(76561198249527954, "steam-token");

    private static Harness NewHarness()
    {
        var time = new FakeTimeProvider(Origin);
        var options = new AppOptions
        {
            PublicBaseUrl = "https://creds.example.org"
        };
        var store = new SessionStore(options, time);
        var steps = new FakeRegistrationSteps();
        var flow = new CredentialFlow(steps, store, options, time, NullLogger<CredentialFlow>.Instance);
        return new Harness(flow, store, steps, time, options);
    }

    /// <summary>Drains the buffered events. The stream is open-ended while the session lives, so a
    /// short window is what stops the enumeration.</summary>
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
    public async Task CompleteRegistrationAsync_RunsStepsInTheReorderedSequence()
    {
        var h = NewHarness();
        using var _s = h.Store;
        h.Store.TryCreate("203.0.113.7", isLocal: true, out var session, out _);

        await h.Flow.CompleteRegistrationAsync(session!, Login(), CancellationToken.None);

        Assert.Equal(
            [
                nameof(FakeRegistrationSteps.AcquireDeviceCredentialsAsync),
                nameof(FakeRegistrationSteps.RegisterWithCompanionAsync)
            ],
            h.Steps.Calls);
        Assert.Equal("steam-token", h.Steps.SteamTokenSeen);
    }

    [Fact]
    public async Task CompleteRegistrationAsync_ReachesReadyAndStoresCredentials()
    {
        var h = NewHarness();
        using var _s = h.Store;
        h.Store.TryCreate("203.0.113.7", isLocal: true, out var session, out _);

        await h.Flow.CompleteRegistrationAsync(session!, Login(), CancellationToken.None);

        Assert.Equal(SessionState.Ready, session!.State);
        Assert.NotNull(session.Credentials);
        Assert.Equal(Origin.Add(h.Options.SessionTtl), session.ExpiresAt);
    }

    [Fact]
    public async Task CompleteRegistrationAsync_DropsTheSteamTokenOnSuccess()
    {
        var h = NewHarness();
        using var _s = h.Store;
        h.Store.TryCreate("203.0.113.7", isLocal: true, out var session, out _);

        await h.Flow.CompleteRegistrationAsync(session!, Login(), CancellationToken.None);

        Assert.Null(session!.SteamToken);
        Assert.Equal(76561198249527954UL, session.SteamId);
    }

    [Fact]
    public async Task CompleteRegistrationAsync_PublishesCredentialsWithSteamIdAsString()
    {
        var h = NewHarness();
        using var _s = h.Store;
        h.Store.TryCreate("203.0.113.7", isLocal: true, out var session, out _);

        await h.Flow.CompleteRegistrationAsync(session!, Login(), CancellationToken.None);

        var events = await EventsOfAsync(session!);
        var payload = Assert.IsType<CredentialsPayload>(
            Assert.Single(events, e => e.Type == "credentials").Data);

        Assert.Equal("76561198249527954", payload.SteamId);
        Assert.Contains("ExponentPushToken", payload.ConfigJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteRegistrationAsync_RecordsACompletionForTheAddress()
    {
        var h = NewHarness();
        using var _s = h.Store;
        h.Options.MaxCompletionsPerIpPerHour = 1;
        h.Store.TryCreate("203.0.113.7", isLocal: true, out var session, out _);

        await h.Flow.CompleteRegistrationAsync(session!, Login(), CancellationToken.None);
        h.Store.Remove(session!.SessionId);

        Assert.False(h.Store.TryCreate("203.0.113.7", isLocal: true, out _, out var failure));
        Assert.Equal(SessionCreateFailure.HourlyLimit, failure);
    }

    [Fact]
    public async Task CompleteRegistrationAsync_FailsWhenDeviceRegistrationThrows()
    {
        var h = NewHarness();
        using var _s = h.Store;
        h.Steps.AcquireFailure = new HttpRequestException("upstream down");
        h.Store.TryCreate("203.0.113.7", isLocal: true, out var session, out _);

        await h.Flow.CompleteRegistrationAsync(session!, Login(), CancellationToken.None);

        Assert.Equal(SessionState.Failed, session!.State);
        Assert.Null(session.SteamToken);

        var events = await EventsOfAsync(session);
        var error = Assert.IsType<ErrorPayload>(Assert.Single(events, e => e.Type == "error").Data);
        Assert.DoesNotContain("upstream down", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteRegistrationAsync_FailsWhenCompanionRegistrationThrows()
    {
        var h = NewHarness();
        using var _s = h.Store;
        h.Steps.CompanionFailure = new HttpRequestException("rejected");
        h.Store.TryCreate("203.0.113.7", isLocal: true, out var session, out _);

        await h.Flow.CompleteRegistrationAsync(session!, Login(), CancellationToken.None);

        Assert.Equal(SessionState.Failed, session!.State);
        Assert.Null(session.SteamToken);
    }

    [Fact]
    public async Task CompleteRegistrationAsync_FailsWhenExpoTokenIsMissing()
    {
        var h = NewHarness();
        using var _s = h.Store;
        h.Steps.CredentialsToReturn = new RustPlusApi.Fcm.Data.Credentials
        {
            ExpoPushToken = null
        };
        h.Store.TryCreate("203.0.113.7", isLocal: true, out var session, out _);

        await h.Flow.CompleteRegistrationAsync(session!, Login(), CancellationToken.None);

        Assert.Equal(SessionState.Failed, session!.State);
        Assert.DoesNotContain(nameof(FakeRegistrationSteps.RegisterWithCompanionAsync), h.Steps.Calls);
    }

    [Fact]
    public async Task CompleteRegistrationAsync_FailsWhenTheSteamTokenIsGoneByCompanionRegistration()
    {
        // The token is dropped as soon as it has no further use, and disposal drops it too, so
        // between device registration and the companion call it can legitimately be null. That
        // must land the session in Failed rather than dereference null.
        var h = NewHarness();
        using var _s = h.Store;
        h.Store.TryCreate("203.0.113.7", isLocal: true, out var session, out _);
        h.Steps.OnAcquire = session!.ClearSteamToken;

        await h.Flow.CompleteRegistrationAsync(session, Login(), CancellationToken.None);

        Assert.Equal(SessionState.Failed, session.State);
        Assert.DoesNotContain(nameof(FakeRegistrationSteps.RegisterWithCompanionAsync), h.Steps.Calls);
    }

    [Fact]
    public async Task CompleteRegistrationAsync_StaysQuietWhenCancelled()
    {
        var h = NewHarness();
        using var _s = h.Store;
        h.Steps.AcquireFailure = new OperationCanceledException();
        h.Store.TryCreate("203.0.113.7", isLocal: true, out var session, out _);

        await h.Flow.CompleteRegistrationAsync(session!, Login(), CancellationToken.None);

        var events = await EventsOfAsync(session!);
        Assert.DoesNotContain(events, e => e.Type == "error");
    }

    private sealed record Harness(
        CredentialFlow Flow,
        SessionStore Store,
        FakeRegistrationSteps Steps,
        FakeTimeProvider Time,
        AppOptions Options);
}
