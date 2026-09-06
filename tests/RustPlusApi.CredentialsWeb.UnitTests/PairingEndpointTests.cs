using Microsoft.Extensions.DependencyInjection;
using RustPlusApi.CredentialsWeb.Endpoints;
using RustPlusApi.CredentialsWeb.Sessions;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class PairingEndpointTests
{
    private static Uri PairingUri(string sessionId) =>
        new($"/api/sessions/{sessionId}/pairing", UriKind.Relative);

    /// <summary>Runs a session all the way to Ready through the real callback route.</summary>
    /// <param name="factory">The test host to create a session and client against.</param>
    private static async Task<Session> ReadySessionAsync(CredentialsWebFactory factory)
    {
        using var client = factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

        var store = factory.Services.GetRequiredService<SessionStore>();
        store.TryCreate("203.0.113.7", isLocal: true, out var session, out _);

        await client.GetAsync(new Uri(
            $"/callback/{session!.ReturnToken}?steamId=76561198249527954&token=steam-token",
            UriKind.Relative));
        await session.BackgroundWork;

        factory.Steps.Calls.Clear();
        return session;
    }

    /// <summary>Runs a remote session to Ready through the paste route, the way a hosted visitor
    /// does.</summary>
    /// <param name="factory">The test host to create a session and client against.</param>
    private static async Task<Session> RemoteReadySessionAsync(CredentialsWebFactory factory)
    {
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<SessionStore>();
        store.TryCreate("203.0.113.7", isLocal: false, out var session, out _);

        await client.PostAsJsonAsync(
            new Uri("/api/callback", UriKind.Relative),
            new PasteCallbackRequest(
                $"http://localhost:54321/callback/{session!.ReturnToken}"
                + "?steamId=76561198249527954&token=steam-token"));
        await session.BackgroundWork;

        factory.Steps.Calls.Clear();
        return session;
    }

    [Fact]
    public async Task Pairing_Returns403_ForARemoteSession()
    {
        await using var factory = new CredentialsWebFactory();
        var session = await RemoteReadySessionAsync(factory);
        using var client = factory.CreateClient();

        var response = await client.PostAsync(PairingUri(session.SessionId), null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.Steps.Calls);
    }

    [Fact]
    public async Task Pairing_IsAllowedForARemoteSession_WhenTheOperatorOptsIn()
    {
        await using var factory = new CredentialsWebFactory(new Dictionary<string, string>
        {
            ["CredentialsWeb__AllowRemotePairing"] = "true"
        });
        factory.Steps.PairingWaitsForGate = true;
        var session = await RemoteReadySessionAsync(factory);
        using var client = factory.CreateClient();

        var response = await client.PostAsync(PairingUri(session.SessionId), null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Pairing_Returns404_ForAnUnknownSession()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(PairingUri("0123456789abcdef0123456789abcdef"), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Pairing_Returns409_WhenTheSessionIsNotReady()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<SessionStore>();
        store.TryCreate("203.0.113.7", isLocal: true, out var session, out _);

        var response = await client.PostAsync(PairingUri(session!.SessionId), null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Pairing_Returns202AndStartsTheWait()
    {
        await using var factory = new CredentialsWebFactory();
        factory.Steps.PairingWaitsForGate = true;
        var session = await ReadySessionAsync(factory);
        using var client = factory.CreateClient();

        var response = await client.PostAsync(PairingUri(session.SessionId), null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        // Bounded: a regression that never reaches AwaitingPairing must fail as an assertion,
        // not park a foreground thread and hang the test process.
        using var reachTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (session.State != SessionState.AwaitingPairing)
        {
            Assert.False(reachTimeout.IsCancellationRequested, "The flow never reached AwaitingPairing.");
            await Task.Delay(10);
        }

        factory.Steps.PairingGate.SetResult();
        await session.BackgroundWork;
        Assert.Equal(SessionState.Paired, session.State);
    }

    [Fact]
    public async Task Pairing_Returns429_WhenThePairingCapIsFull()
    {
        await using var factory = new CredentialsWebFactory(
            new Dictionary<string, string>
            {
                ["CredentialsWeb__MaxConcurrentPairings"] = "1"
            });
        var session = await ReadySessionAsync(factory);
        using var client = factory.CreateClient();

        var store = factory.Services.GetRequiredService<SessionStore>();
        store.TryAcquirePairingSlot();

        var response = await client.PostAsync(PairingUri(session.SessionId), null);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Empty(factory.Steps.Calls);
    }

    [Fact]
    public async Task Pairing_ReleasesTheSlotOnceTheWaitFinishes()
    {
        await using var factory = new CredentialsWebFactory();
        var session = await ReadySessionAsync(factory);
        using var client = factory.CreateClient();

        await client.PostAsync(PairingUri(session.SessionId), null);
        await session.BackgroundWork;

        var store = factory.Services.GetRequiredService<SessionStore>();
        Assert.Equal(0, store.ActivePairings);
    }
}
