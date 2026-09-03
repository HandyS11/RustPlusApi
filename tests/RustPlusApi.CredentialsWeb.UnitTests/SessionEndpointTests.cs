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
            Uri.EscapeDataString($"{CredentialsWebFactory.BaseUrl}/callback/"),
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
            new Dictionary<string, string> { ["CredentialsWeb__MaxConcurrentSessions"] = "1" });
        using var client = factory.CreateClient();

        // Occupy the only slot from a different address, and move it out of Created so the
        // eviction rule cannot reclaim it.
        var store = factory.Services.GetRequiredService<SessionStore>();
        store.TryCreate("198.51.100.1", out var occupant, out _);
        occupant!.Advance(SessionState.Authenticated, factory.Time.GetUtcNow().AddMinutes(15));

        var response = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("docker run", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Responses_CarryTheSecurityHeaders()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(new Uri("/api/sessions", UriKind.Relative), null);

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
}
