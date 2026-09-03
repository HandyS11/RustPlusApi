using Microsoft.Extensions.DependencyInjection;
using RustPlusApi.CredentialsWeb.Sessions;
using System.Net;
using Xunit;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class EventEndpointTests
{
    /// <summary>Reads SSE frames until <paramref name="count"/> <c>event:</c> lines have arrived.</summary>
    /// <param name="client">The client to read the stream with.</param>
    /// <param name="sessionId">The session whose event stream to open.</param>
    /// <param name="count">How many <c>event:</c> lines to wait for.</param>
    /// <returns>The <c>event:</c> names seen, in arrival order.</returns>
    private static async Task<List<string>> ReadEventNamesAsync(HttpClient client, string sessionId, int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await client.GetAsync(
            new Uri($"/api/sessions/{sessionId}/events", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);

        response.EnsureSuccessStatusCode();

        var names = new List<string>();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var reader = new StreamReader(stream);

        while (names.Count < count)
        {
            var line = await reader.ReadLineAsync(timeout.Token);
            if (line is null)
            {
                break;
            }

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                names.Add(line["event: ".Length..]);
            }
        }

        return names;
    }

    private static Session NewSession(CredentialsWebFactory factory)
    {
        var store = factory.Services.GetRequiredService<SessionStore>();
        store.TryCreate("203.0.113.7", out var session, out _);
        return session!;
    }

    [Fact]
    public async Task Events_Returns404_ForAnUnknownSession()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            new Uri("/api/sessions/0123456789abcdef0123456789abcdef/events", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Events_UsesTheEventStreamContentType()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();
        var session = NewSession(factory);
        session.Advance(SessionState.Authenticated, DateTimeOffset.MaxValue);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await client.GetAsync(
            new Uri($"/api/sessions/{session.SessionId}/events", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Events_ReplaysEventsPublishedBeforeTheStreamOpened()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();
        var session = NewSession(factory);
        session.Advance(SessionState.Authenticated, DateTimeOffset.MaxValue);
        session.Advance(SessionState.Registering, DateTimeOffset.MaxValue);

        var names = await ReadEventNamesAsync(client, session.SessionId, 2);

        Assert.Equal(["step", "step"], names);
    }

    [Fact]
    public async Task Events_ReplaysTheSameHistoryToAReconnectingClient()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();
        var session = NewSession(factory);
        session.Advance(SessionState.Authenticated, DateTimeOffset.MaxValue);

        var first = await ReadEventNamesAsync(client, session.SessionId, 1);
        var second = await ReadEventNamesAsync(client, session.SessionId, 1);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Events_CarriesTheJsonPayload()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();
        var session = NewSession(factory);
        session.Advance(SessionState.Registering, DateTimeOffset.MaxValue);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await client.GetAsync(
            new Uri($"/api/sessions/{session.SessionId}/events", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var reader = new StreamReader(stream);

        await reader.ReadLineAsync(timeout.Token);
        var dataLine = await reader.ReadLineAsync(timeout.Token);

        Assert.Equal("""data: {"state":"Registering"}""", dataLine);
    }
}
