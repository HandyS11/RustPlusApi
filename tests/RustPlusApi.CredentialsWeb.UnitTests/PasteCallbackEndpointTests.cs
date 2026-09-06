using Microsoft.Extensions.DependencyInjection;
using RustPlusApi.CredentialsWeb.Endpoints;
using RustPlusApi.CredentialsWeb.Sessions;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class PasteCallbackEndpointTests
{
    private static readonly Uri Route = new("/api/callback", UriKind.Relative);

    private static Session NewSession(CredentialsWebFactory factory)
    {
        var store = factory.Services.GetRequiredService<SessionStore>();
        store.TryCreate("203.0.113.7", isLocal: false, out var session, out _);
        return session!;
    }

    private static string Pasted(string returnToken) =>
        $"http://localhost:54321/callback/{returnToken}?steamId=76561198249527954&token=steam-token";

    /// <summary>The address Facepunch leaves in the visitor's address bar for a given login URL,
    /// with the Steam identity Facepunch appends to it.</summary>
    /// <param name="loginUrl">The login URL from the create-session response.</param>
    private static string PastedFrom(string loginUrl)
    {
        const string marker = "?returnUrl=";
        var index = loginUrl.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        return Uri.UnescapeDataString(loginUrl[index..])
               + "?steamId=76561198249527954&token=steam-token";
    }

    [Fact]
    public async Task Paste_StillCompletes_WhenAFreshTabStartedASecondSessionFirst()
    {
        // The recovery this flow exists for. The visitor comes back in a tab with no session handle
        // — a fresh one, or the tab that failed to load — and has to press Start to reach the paste
        // box at all, which evicts the session they are mid-login on. Their pasted address must
        // still land, and land on the session that tab is watching, or the Steam login they have
        // already completed is unrecoverable.
        await using var factory = new CredentialsWebFactory
        {
            RemoteIpAddress = IPAddress.Parse("203.0.113.7")
        };
        using var client = factory.CreateClient();

        var first = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);
        var firstBody = await first.Content.ReadFromJsonAsync<CreateSessionResponse>();
        var second = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);
        var secondBody = await second.Content.ReadFromJsonAsync<CreateSessionResponse>();

        var response = await client.PostAsJsonAsync(
            Route,
            new PasteCallbackRequest(PastedFrom(firstBody!.LoginUrl)));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PasteCallbackResponse>();
        Assert.Equal(secondBody!.SessionId, body!.SessionId);

        var store = factory.Services.GetRequiredService<SessionStore>();
        Assert.True(store.TryGet(secondBody.SessionId, out var session));
        await session!.BackgroundWork;
        Assert.Equal(SessionState.Ready, session.State);
        Assert.Equal("steam-token", factory.Steps.SteamTokenSeen);
    }

    [Fact]
    public async Task Paste_DrivesTheFlowToReady()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();
        var session = NewSession(factory);

        var response = await client.PostAsJsonAsync(Route, new PasteCallbackRequest(Pasted(session.ReturnToken)));
        await session.BackgroundWork;

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PasteCallbackResponse>();
        Assert.Equal(session.SessionId, body!.SessionId);
        Assert.Equal(SessionState.Ready, session.State);
        Assert.Equal("steam-token", factory.Steps.SteamTokenSeen);
        Assert.Null(session.SteamToken);
    }

    [Fact]
    public async Task Paste_NamesTheSessionOnTheWireInCamelCase()
    {
        // app.js reads `sessionId` off this response to follow the session the pasted address named,
        // which is not necessarily the one the tab created. Deserializing into PasteCallbackResponse
        // is case-insensitive and so cannot catch a rename; the property name has to be asserted raw.
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();
        var session = NewSession(factory);

        var response = await client.PostAsJsonAsync(Route, new PasteCallbackRequest(Pasted(session.ReturnToken)));
        await session.BackgroundWork;

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains($"\"sessionId\":\"{session.SessionId}\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Paste_Returns404_ForAnUnknownReturnToken()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            Route, new PasteCallbackRequest(Pasted("0123456789abcdef0123456789abcdef")));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Paste_Returns404_WhenReplayed()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();
        var session = NewSession(factory);

        var first = await client.PostAsJsonAsync(Route, new PasteCallbackRequest(Pasted(session.ReturnToken)));
        await session.BackgroundWork;
        var second = await client.PostAsJsonAsync(Route, new PasteCallbackRequest(Pasted(session.ReturnToken)));

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("https://companion-rust.facepunch.com/login")]
    public async Task Paste_Returns400_AndConsumesNothing_ForAnUnreadableAddress(string pasted)
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();
        var session = NewSession(factory);

        var bad = await client.PostAsJsonAsync(Route, new PasteCallbackRequest(pasted));

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(SessionState.Created, session.State);
        Assert.Empty(factory.Steps.Calls);

        // The whole point of parsing before consuming: the visitor gets to correct the paste.
        var good = await client.PostAsJsonAsync(Route, new PasteCallbackRequest(Pasted(session.ReturnToken)));
        await session.BackgroundWork;

        Assert.Equal(HttpStatusCode.Accepted, good.StatusCode);
        Assert.Equal(SessionState.Ready, session.State);
    }

    [Fact]
    public async Task Paste_Returns400_AndConsumesNothing_WhenALiveReturnTokenFailsToParse()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();
        var session = NewSession(factory);

        // The theory above cannot prove parse-before-consume on its own: none of its rows carries a
        // return token, so a consume-then-parse implementation would pass it with nothing to consume.
        // This address carries the session's real token and still has to fail — the path shape is
        // right and the token is live, but ParseCallback rejects it for the missing steamId/token
        // query. A consume-first implementation would burn the session here.
        var bad = await client.PostAsJsonAsync(
            Route,
            new PasteCallbackRequest($"http://localhost:54321/callback/{session.ReturnToken}"));

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(SessionState.Created, session.State);
        Assert.Empty(factory.Steps.Calls);

        var good = await client.PostAsJsonAsync(Route, new PasteCallbackRequest(Pasted(session.ReturnToken)));
        await session.BackgroundWork;

        Assert.Equal(HttpStatusCode.Accepted, good.StatusCode);
        Assert.Equal(SessionState.Ready, session.State);
    }

    [Fact]
    public async Task Paste_Returns400_ForANullUrl()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(Route, new PasteCallbackRequest(null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
