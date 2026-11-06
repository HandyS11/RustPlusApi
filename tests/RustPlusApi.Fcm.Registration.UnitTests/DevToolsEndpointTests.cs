using Xunit;
using System.Net;
using RustPlusApi.Fcm.Registration.Steps.Cdp;
using RustPlusApi.Fcm.Registration.UnitTests.TestHelpers;

namespace RustPlusApi.Fcm.Registration.UnitTests;

public class DevToolsEndpointTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body)
        };

    [Fact]
    public async Task WaitForPageAsync_PollsPastFailuresUntilAPageAppears()
    {
        var handler = new StubHttpMessageHandler((_, call) => call switch
        {
            0 => throw new HttpRequestException("refused"),
            1 => new HttpResponseMessage(HttpStatusCode.NotFound),
            2 => Json("""[{"type":"service_worker","webSocketDebuggerUrl":"ws://127.0.0.1:9333/devtools/page/S"}]"""),
            _ => Json(
                """[{"type":"service_worker"},{"type":"page","webSocketDebuggerUrl":"ws://127.0.0.1:9333/devtools/page/X"}]"""),
        });
        using var http = handler.CreateClient();

        var uri = await DevToolsEndpoint.WaitForPageAsync(
                http, 9333, TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(5), CancellationToken.None)
            .WaitAsync(TestTimeout);

        Assert.Equal(new Uri("ws://127.0.0.1:9333/devtools/page/X"), uri);
        Assert.Equal(4, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal("http://127.0.0.1:9333/json/list", r.RequestUri!.ToString()));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[null]")]
    [InlineData("""[{"type":"service_worker"}]""")]
    [InlineData("""[{"type":"page"}]""")]
    // Malformed entries: not an object, or fields that are not strings.
    [InlineData("[1]")]
    [InlineData("""["page"]""")]
    [InlineData("[[]]")]
    [InlineData("""[{"type":1,"webSocketDebuggerUrl":"ws://127.0.0.1:1/x"}]""")]
    [InlineData("""[{"type":{},"webSocketDebuggerUrl":"ws://127.0.0.1:1/x"}]""")]
    [InlineData("""[{"type":"page","webSocketDebuggerUrl":5}]""")]
    [InlineData("""[{"type":"page","webSocketDebuggerUrl":{}}]""")]
    // Addresses that are not an absolute ws:// address on the loopback host at the probed port.
    [InlineData("""[{"type":"page","webSocketDebuggerUrl":"not a uri"}]""")]
    [InlineData("""[{"type":"page","webSocketDebuggerUrl":"ws://127.0.0.1 bad:1/x"}]""")]
    [InlineData("""[{"type":"page","webSocketDebuggerUrl":"/devtools/page/x"}]""")]
    [InlineData("""[{"type":"page","webSocketDebuggerUrl":"wss://127.0.0.1:1/x"}]""")]
    [InlineData("""[{"type":"page","webSocketDebuggerUrl":"http://127.0.0.1:1/x"}]""")]
    [InlineData("""[{"type":"page","webSocketDebuggerUrl":"ws://example.com:1/x"}]""")]
    [InlineData("""[{"type":"page","webSocketDebuggerUrl":"ws://10.0.0.1:1/x"}]""")]
    [InlineData("""[{"type":"page","webSocketDebuggerUrl":"ws://127.0.0.1:2/x"}]""")]
    [InlineData("""[{"type":"page","webSocketDebuggerUrl":"ws://127.0.0.1/x"}]""")]
    public async Task WaitForPageAsync_UnusableBody_KeepsPolling(string unusable)
    {
        var handler = new StubHttpMessageHandler((_, call) => call == 0
            ? Json(unusable)
            : Json("""[{"type":"page","webSocketDebuggerUrl":"ws://127.0.0.1:1/p"}]"""));
        using var http = handler.CreateClient();

        var uri = await DevToolsEndpoint.WaitForPageAsync(
                http, 1, TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(5), CancellationToken.None)
            .WaitAsync(TestTimeout);

        Assert.Equal(new Uri("ws://127.0.0.1:1/p"), uri);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData("ws://127.0.0.1:1/p")]
    [InlineData("ws://localhost:1/p")]
    [InlineData("ws://[::1]:1/p")]
    public async Task WaitForPageAsync_AcceptsLoopbackAddressesAtTheProbedPort(string address)
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Json($$"""[{"type":"page","webSocketDebuggerUrl":"{{address}}"}]"""));
        using var http = handler.CreateClient();

        var uri = await DevToolsEndpoint.WaitForPageAsync(
                http, 1, TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(5), CancellationToken.None)
            .WaitAsync(TestTimeout);

        Assert.Equal(new Uri(address), uri);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task WaitForPageAsync_SkipsAForgedEntryForALaterUsableOne()
    {
        var handler = new StubHttpMessageHandler((_, _) => Json(
            """[{"type":"page","webSocketDebuggerUrl":"ws://example.com:1/x"},{"type":"page","webSocketDebuggerUrl":"ws://127.0.0.1:1/p"}]"""));
        using var http = handler.CreateClient();

        var uri = await DevToolsEndpoint.WaitForPageAsync(
                http, 1, TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(5), CancellationToken.None)
            .WaitAsync(TestTimeout);

        Assert.Equal(new Uri("ws://127.0.0.1:1/p"), uri);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task WaitForPageAsync_NoPageWithinTimeout_ThrowsTimeoutException()
    {
        var handler = StubHttpMessageHandler.Always(HttpStatusCode.NotFound, string.Empty);
        using var http = handler.CreateClient();

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => DevToolsEndpoint.WaitForPageAsync(
                http, 9333, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10), CancellationToken.None)
            .WaitAsync(TestTimeout));

        Assert.Equal("The browser did not open its debugging port within 1 seconds.", ex.Message);
    }

    [Fact]
    public async Task WaitForPageAsync_CallerCancels_ThrowsOperationCanceledNotTimeout()
    {
        var handler = StubHttpMessageHandler.Always(HttpStatusCode.NotFound, string.Empty);
        using var http = handler.CreateClient();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DevToolsEndpoint.WaitForPageAsync(
            http, 9333, TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(10), cts.Token).WaitAsync(TestTimeout));
    }
}
