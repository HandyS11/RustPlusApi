using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using RustPlusApi.CredentialsWeb.Endpoints;
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
        store.TryCreate("203.0.113.7", isLocal: true, out var session, out _);
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

    [Fact]
    public async Task Callback_WithAMalformedCallback_ExtendsTheSessionExpiryPastItsOriginalCreatedTtl()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = NoRedirectClient(factory);
        var session = NewSession(factory);
        var originalExpiry = session.ExpiresAt;

        await client.GetAsync(
            new Uri($"/callback/{session.ReturnToken}?steamId=76561198249527954", UriKind.Relative));

        // A real Steam round-trip (2FA included) can consume most of the short CreatedTtl the session
        // was created with. A Failed transition that kept that original expiry would risk the sweeper
        // removing the session — and its error event — before the visitor's browser reads it back. So
        // this must use the same SessionTtl-based deadline as every other Failed transition.
        Assert.True(
            session.ExpiresAt > originalExpiry,
            $"Expected the expiry to move past {originalExpiry:O}, but it stayed at {session.ExpiresAt:O}.");
    }

    [Fact]
    public void TryParseSteamLogin_ReturnsTrue_ForAWellFormedCallback()
    {
        var parsed = CallbackEndpoints.TryParseSteamLogin(
            CredentialsWebFactory.BaseUrl,
            new PathString("/callback/abc123"),
            new QueryString("?steamId=76561198249527954&token=steam-token"),
            out var login);

        Assert.True(parsed);
        Assert.NotNull(login);
        Assert.Equal(76561198249527954UL, login.SteamId);
        Assert.Equal("steam-token", login.Token);
    }

    [Fact]
    public void TryParseSteamLogin_ReturnsFalse_WhenBuildingTheUriThrowsUriFormatException()
    {
        // A malformed host is unreachable through a real request — PublicBaseUrl is validated
        // absolute at startup, and Kestrel has already rejected anything that would leave the path or
        // query URI-illegal by the time a request is accepted. Forcing it here, directly against the
        // extracted method, is the only way to exercise this branch without a contrived HTTP test.
        var parsed = CallbackEndpoints.TryParseSteamLogin(
            "https://exa mple.org",
            new PathString("/callback/abc123"),
            new QueryString("?steamId=76561198249527954&token=steam-token"),
            out var login);

        Assert.False(parsed);
        Assert.Null(login);
    }
}
