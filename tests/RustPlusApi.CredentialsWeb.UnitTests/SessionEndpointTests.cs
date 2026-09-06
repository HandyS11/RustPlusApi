using Microsoft.Extensions.DependencyInjection;
using RustPlusApi.CredentialsWeb.Endpoints;
using RustPlusApi.CredentialsWeb.Sessions;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class SessionEndpointTests
{
    [Fact]
    public async Task CreateSession_ReturnsSessionIdAndFacepunchLoginUrl()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CreateSessionResponse>();

        Assert.NotNull(body);
        Assert.Matches("^[0-9a-f]{32}$", body.SessionId);
        Assert.StartsWith(
            "https://companion-rust.facepunch.com/login?returnUrl=",
            body.LoginUrl,
            StringComparison.Ordinal);
        Assert.Contains(
            Uri.EscapeDataString("http://localhost/callback/"),
            body.LoginUrl,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateSession_ReturnsADifferentReturnTokenEachTime()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();

        var first = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);
        var firstBody = await first.Content.ReadFromJsonAsync<CreateSessionResponse>();
        var second = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);
        var secondBody = await second.Content.ReadFromJsonAsync<CreateSessionResponse>();

        Assert.NotEqual(firstBody!.LoginUrl, secondBody!.LoginUrl);
    }

    [Fact]
    public async Task CreateSession_TheLoginUrlNeverCarriesTheSessionId()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);
        var body = await response.Content.ReadFromJsonAsync<CreateSessionResponse>();

        Assert.DoesNotContain(body!.SessionId, body.LoginUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateSession_EvictsThisAddressesAbandonedCreatedSession()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<SessionStore>();

        var first = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);
        var firstBody = await first.Content.ReadFromJsonAsync<CreateSessionResponse>();

        var second = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);
        second.EnsureSuccessStatusCode();

        Assert.False(store.TryGet(firstBody!.SessionId, out _));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public async Task CreateSession_Returns429WithASelfHostPointer_WhenTheGlobalCapIsFull()
    {
        await using var factory = new CredentialsWebFactory(
            new Dictionary<string, string>
            {
                ["CredentialsWeb__MaxConcurrentSessions"] = "1"
            });
        using var client = factory.CreateClient();

        // Occupy the only slot from a different address, and move it out of Created so the
        // eviction rule cannot reclaim it.
        var store = factory.Services.GetRequiredService<SessionStore>();
        store.TryCreate("198.51.100.1", isLocal: true, out var occupant, out _);
        occupant!.Advance(SessionState.Authenticated, factory.Time.GetUtcNow().AddMinutes(15));

        var response = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("docker run", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateSession_EvictsThisAddressesFailedSession()
    {
        // Nothing in a Failed session is resumable, so the app's own "Start over" flow — which
        // leads straight back into POST /api/sessions — must not be refused because of it.
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<SessionStore>();

        var first = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);
        var firstBody = await first.Content.ReadFromJsonAsync<CreateSessionResponse>();
        store.TryGet(firstBody!.SessionId, out var failedSession);
        failedSession!.Advance(SessionState.Failed, factory.Time.GetUtcNow().AddMinutes(15));

        var second = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);

        second.EnsureSuccessStatusCode();
        Assert.False(store.TryGet(firstBody.SessionId, out _));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public async Task CreateSession_Returns429WithADistinctMessage_WhenThisAddressHasAResumableSession()
    {
        // Distinct from the generic "at capacity" 429: a session past Created and not Failed
        // carries real, resumable upstream work, and the false "at capacity" framing sent a visitor
        // into a needless wait instead of pointing them back at the session they already have.
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<SessionStore>();

        var first = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);
        var firstBody = await first.Content.ReadFromJsonAsync<CreateSessionResponse>();
        store.TryGet(firstBody!.SessionId, out var activeSession);
        activeSession!.Advance(SessionState.Authenticated, factory.Time.GetUtcNow().AddMinutes(15));

        var response = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("already have a session", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("docker run", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Responses_CarryTheSecurityHeaders()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);

        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task ErrorResponses_CarryTheSecurityHeadersToo()
    {
        // The security-headers middleware sits ahead of the endpoint and writes via OnStarting, so
        // it should apply no matter what status code the endpoint eventually returns — including
        // the 429 capacity response, where a missing Cache-Control: no-store would matter most.
        await using var factory = new CredentialsWebFactory(
            new Dictionary<string, string>
            {
                ["CredentialsWeb__MaxConcurrentSessions"] = "1"
            });
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<SessionStore>();
        store.TryCreate("198.51.100.1", isLocal: true, out var occupant, out _);
        occupant!.Advance(SessionState.Authenticated, factory.Time.GetUtcNow().AddMinutes(15));

        var response = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateSession_IgnoresForwardedFor_WhenNoProxyIsConfigured()
    {
        // With no CredentialsWeb__KnownProxies set, a caller must not be able to pick its own
        // per-IP bucket by sending an arbitrary X-Forwarded-For. Both requests below claim a
        // different address; if that were honored they would land in different buckets and both
        // sessions would survive. Ignored (the safe default), both resolve to the same real
        // connection address, so the second creation evicts the first's abandoned Created session.
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<SessionStore>();

        using var first = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/sessions", UriKind.Relative));
        first.Headers.Add("X-Forwarded-For", "203.0.113.7");
        (await client.SendAsync(first)).EnsureSuccessStatusCode();

        using var second = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/sessions", UriKind.Relative));
        second.Headers.Add("X-Forwarded-For", "198.51.100.42");
        var response = await client.SendAsync(second);

        response.EnsureSuccessStatusCode();
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public async Task CreateSession_AppliesForwardedFor_WhenAProxyIsConfigured()
    {
        // The inverse of CreateSession_IgnoresForwardedFor_WhenNoProxyIsConfigured. Once the
        // operator names their proxy, a forwarded address must actually be applied — otherwise
        // every visitor behind that proxy would still collapse into the one shared per-IP bucket
        // the setting exists to prevent, just silently instead of loudly. Both requests below
        // claim a different address; correctly applied, they land in two separate buckets and
        // both sessions survive (Count == 2), the opposite of the no-proxy-configured case above.
        await using var factory = new CredentialsWebFactory(
            new Dictionary<string, string>
            {
                ["CredentialsWeb__KnownProxies__0"] = "127.0.0.1"
            });
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<SessionStore>();

        using var first = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/sessions", UriKind.Relative));
        first.Headers.Add("X-Forwarded-For", "203.0.113.7");
        (await client.SendAsync(first)).EnsureSuccessStatusCode();

        using var second = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/sessions", UriKind.Relative));
        second.Headers.Add("X-Forwarded-For", "198.51.100.42");
        var response = await client.SendAsync(second);

        response.EnsureSuccessStatusCode();
        Assert.Equal(2, store.Count);
    }

    private static Uri ReturnUrlOf(string loginUrl)
    {
        const string marker = "?returnUrl=";
        var index = loginUrl.IndexOf(marker, StringComparison.Ordinal);
        return new Uri(Uri.UnescapeDataString(loginUrl[(index + marker.Length)..]));
    }

    private static HttpClient HostedClient(CredentialsWebFactory factory)
    {
        factory.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        return factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://creds.example.org")
        });
    }

    [Fact]
    public async Task CreateSession_Local_ReturnsARedirectModeUrlPointingAtThisRequestsOrigin()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);
        var body = await response.Content.ReadFromJsonAsync<CreateSessionResponse>();

        Assert.Equal("redirect", body!.CallbackMode);
        Assert.True(body.PairingAvailable);
        var returnUrl = ReturnUrlOf(body.LoginUrl);
        Assert.Equal("localhost", returnUrl.Host);
        Assert.StartsWith("/callback/", returnUrl.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateSession_Hosted_ReturnsAPasteModeUrlPointingAtADeadLoopbackPort()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = HostedClient(factory);

        var response = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);
        var body = await response.Content.ReadFromJsonAsync<CreateSessionResponse>();

        Assert.Equal("paste", body!.CallbackMode);
        Assert.False(body.PairingAvailable);

        var returnUrl = ReturnUrlOf(body.LoginUrl);
        Assert.Equal("localhost", returnUrl.Host);
        Assert.Equal(Uri.UriSchemeHttp, returnUrl.Scheme);
        // The dynamic range: very unlikely to belong to something the visitor actually runs.
        Assert.InRange(returnUrl.Port, 49152, 65535);
    }

    [Fact]
    public async Task CreateSession_Hosted_TheReturnUrlNeverNamesThePublicHost()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = HostedClient(factory);

        var response = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);
        var body = await response.Content.ReadFromJsonAsync<CreateSessionResponse>();

        Assert.DoesNotContain("creds.example.org", body!.LoginUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_WarnsThatARetiredSettingIsStillConfigured()
    {
        await using var factory = new CredentialsWebFactory(new Dictionary<string, string>
        {
            ["CredentialsWeb__PublicBaseUrl"] = "https://creds.example.org"
        });
        using var client = factory.CreateClient();

        // Force the host to build; the warning is emitted during startup.
        await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);

        Assert.Contains(
            factory.Logs.Records,
            record => record.Contains("PublicBaseUrl", StringComparison.Ordinal)
                      && record.Contains("no longer read", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Startup_SaysWhatAnEmptyKnownProxiesMeansBehindAProxy()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();

        // Force the host to build; the message is emitted during startup.
        await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);

        Assert.Contains(
            factory.Logs.Records,
            record => record.Contains("KnownProxies is empty", StringComparison.Ordinal)
                      && record.Contains("evict each other", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Startup_SaysNothingAboutKnownProxiesWhenOneIsConfigured()
    {
        await using var factory = new CredentialsWebFactory(new Dictionary<string, string>
        {
            ["CredentialsWeb__KnownProxies__0"] = "172.18.0.2"
        });
        using var client = factory.CreateClient();

        await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);

        Assert.DoesNotContain(
            factory.Logs.Records,
            record => record.Contains("KnownProxies is empty", StringComparison.Ordinal));
    }
}
