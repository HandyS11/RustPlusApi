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

    /// <summary>Reads one line, turning a timed-out wait into a named assertion failure instead of
    /// a raw <see cref="OperationCanceledException"/> — so a regression that drops the SSE frame
    /// terminator (or a separating blank line) fails with a message pointing at what broke, rather
    /// than a 10-second <c>TaskCanceledException</c> that looks like a hung test.</summary>
    /// <param name="reader">The stream reader to read a line from.</param>
    /// <param name="timeoutMessage">What to report if the read never completes in time.</param>
    /// <param name="cancellationToken">The bounding timeout token.</param>
    private static async Task<string?> ReadLineOrFailAsync(
        StreamReader reader,
        string timeoutMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            return await reader.ReadLineAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Assert.Fail(timeoutMessage);
            return null; // Unreachable: Assert.Fail always throws.
        }
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

        await ReadLineOrFailAsync(reader, "The SSE frame's event: line never arrived within 10s.", timeout.Token);
        var dataLine = await ReadLineOrFailAsync(
            reader, "The SSE frame's data: line never arrived within 10s.", timeout.Token);
        var terminatorLine = await ReadLineOrFailAsync(
            reader, "SSE frame was not terminated by a blank line within 10s.", timeout.Token);

        Assert.Equal("""data: {"state":"Registering"}""", dataLine);
        // A dropped blank line is silent: EventSource never dispatches an incomplete frame, so this
        // is the one gap the line-by-line assertions above cannot catch on their own.
        Assert.Equal(string.Empty, terminatorLine);
    }

    [Fact]
    public async Task Events_SeparatesConsecutiveFramesWithABlankLine()
    {
        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();
        var session = NewSession(factory);
        session.Advance(SessionState.Authenticated, DateTimeOffset.MaxValue);
        session.Advance(SessionState.Registering, DateTimeOffset.MaxValue);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await client.GetAsync(
            new Uri($"/api/sessions/{session.SessionId}/events", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var reader = new StreamReader(stream);

        var lines = new List<string?>();
        for (var i = 0; i < 6; i++)
        {
            lines.Add(await ReadLineOrFailAsync(
                reader,
                $"Line {i} of the two SSE frames never arrived within 10s — a dropped separator or "
                + "terminator likely stalled the read.",
                timeout.Token));
        }

        // Pins both frames in full, including the blank line that separates them and the one that
        // terminates the second — a dropped separator would merge the two frames into one that
        // EventSource cannot parse, which none of the count-based replay tests above would notice.
        Assert.Equal(
            [
                "event: step",
                """data: {"state":"Authenticated"}""",
                string.Empty,
                "event: step",
                """data: {"state":"Registering"}""",
                string.Empty
            ],
            lines);
    }

    [Fact]
    public async Task Events_SerializesASteam64PayloadFieldAsAQuotedString()
    {
        // Steam64 exceeds Number.MAX_SAFE_INTEGER: an unquoted JSON number would silently corrupt
        // it in the browser. This is the highest-consequence serialization rule in this endpoint.
        const string steam64 = "76561198249527954";

        await using var factory = new CredentialsWebFactory();
        using var client = factory.CreateClient();
        var session = NewSession(factory);
        session.Events.Publish(new SessionEvent("credentials", new CredentialsPayload(steam64, "{}")));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await client.GetAsync(
            new Uri($"/api/sessions/{session.SessionId}/events", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var reader = new StreamReader(stream);

        await reader.ReadLineAsync(timeout.Token);
        var dataLine = await reader.ReadLineAsync(timeout.Token);

        Assert.Equal($$"""data: {"steamId":"{{steam64}}","configJson":"{}"}""", dataLine);
    }
}
