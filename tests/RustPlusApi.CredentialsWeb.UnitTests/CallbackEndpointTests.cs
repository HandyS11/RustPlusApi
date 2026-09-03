using Microsoft.Extensions.DependencyInjection;
using System.Net;
using RustPlusApi.CredentialsWeb.Sessions;
using Xunit;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class CallbackEndpointTests
{
    private static HttpClient NoRedirectClient(CredentialsWebFactory factory) =>
        factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

    private static Session NewSession(CredentialsWebFactory factory)
    {
        var store = factory.Services.GetRequiredService<SessionStore>();
        store.TryCreate("203.0.113.7", out var session, out _);
        return session!;
    }

    private static Uri CallbackUri(string returnToken) =>
        new($"/callback/{returnToken}?steamId=76561198249527954&token=steam-token", UriKind.Relative);

    [Fact]
    public async Task Callback_Redirects302ToTheFragmentCarryingTheSessionId()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = NoRedirectClient(factory);
        var session = NewSession(factory);

        var response = await client.GetAsync(CallbackUri(session.ReturnToken));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal($"/#session={session.SessionId}", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Callback_DrivesTheFlowToReady()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = NoRedirectClient(factory);
        var session = NewSession(factory);

        await client.GetAsync(CallbackUri(session.ReturnToken));
        await session.BackgroundWork;

        Assert.Equal(SessionState.Ready, session.State);
        Assert.Equal("steam-token", factory.Steps.SteamTokenSeen);
        Assert.Null(session.SteamToken);
    }

    [Fact]
    public async Task Callback_Returns404_ForAnUnknownReturnToken()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = NoRedirectClient(factory);

        var response = await client.GetAsync(CallbackUri("0123456789abcdef0123456789abcdef"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Callback_Returns404_WhenReplayed()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = NoRedirectClient(factory);
        var session = NewSession(factory);

        var first = await client.GetAsync(CallbackUri(session.ReturnToken));
        var second = await client.GetAsync(CallbackUri(session.ReturnToken));

        Assert.Equal(HttpStatusCode.Found, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    [Fact]
    public async Task Callback_WithNoToken_FailsTheSessionButStillRedirects()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = NoRedirectClient(factory);
        var session = NewSession(factory);

        var response = await client.GetAsync(
            new Uri($"/callback/{session.ReturnToken}?steamId=76561198249527954", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal($"/#session={session.SessionId}", response.Headers.Location!.ToString());
        Assert.Equal(SessionState.Failed, session.State);
        Assert.Empty(factory.Steps.Calls);
    }

    [Fact]
    public async Task Callback_WithANonNumericSteamId_FailsTheSession()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = NoRedirectClient(factory);
        var session = NewSession(factory);

        await client.GetAsync(
            new Uri($"/callback/{session.ReturnToken}?steamId=not-a-number&token=steam-token", UriKind.Relative));

        Assert.Equal(SessionState.Failed, session.State);
    }
}
