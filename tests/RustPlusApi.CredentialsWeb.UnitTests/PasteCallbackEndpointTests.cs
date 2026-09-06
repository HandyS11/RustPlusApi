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
    public async Task Paste_Returns400_ForANullUrl()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(Route, new PasteCallbackRequest(null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
