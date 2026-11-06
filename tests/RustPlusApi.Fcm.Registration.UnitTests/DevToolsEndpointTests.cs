using Xunit;
using System.Net;
using RustPlusApi.Fcm.Registration.Steps.Cdp;
using RustPlusApi.Fcm.Registration.UnitTests.TestHelpers;

namespace RustPlusApi.Fcm.Registration.UnitTests;

/// <summary>Pins how the library learns the debugging port the browser picked (its <c>DevToolsActivePort</c> file) and
/// how it finds the browser's page on that port.</summary>
public sealed class DevToolsEndpointTests : IDisposable
{
    private const string UsablePortFile = "4242\n/devtools/browser/0b5e3c1e-5f0a-4c4e-9d3a-6c2f1e7a9b10";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A per-test profile directory; it is not created until a test needs it.</summary>
    private readonly string _profile = Path.Combine(Path.GetTempPath(),
        "rustplusapi-devtoolsport-tests-" + Guid.NewGuid().ToString("N"));

    private string PortFile => Path.Combine(_profile, "DevToolsActivePort");

    public void Dispose()
    {
        if (Directory.Exists(_profile))
        {
            Directory.Delete(_profile, recursive: true);
        }
    }

    [Theory]
    [InlineData("9333\n/devtools/browser/0b5e3c1e-5f0a-4c4e-9d3a-6c2f1e7a9b10", 9333)]
    [InlineData("1\n/devtools/browser/b", 1)]
    [InlineData("65535\n/devtools/browser/b", 65535)]
    [InlineData("9333\r\n/devtools/browser/b", 9333)]
    [InlineData("9333\n", 9333)]
    public async Task WaitForActivePortAsync_ReturnsThePortOnTheFirstLine(string contents, int expected)
    {
        WritePortFile(contents);

        var port = await DevToolsEndpoint.WaitForActivePortAsync(
                _profile, TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(5), CancellationToken.None)
            .WaitAsync(TestTimeout);

        Assert.Equal(expected, port);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WaitForActivePortAsync_FileNotWrittenYet_KeepsPollingUntilItAppears(bool profileExists)
    {
        // A missing profile directory and a missing file both mean "not written yet".
        if (profileExists)
        {
            Directory.CreateDirectory(_profile);
        }

        var wait = DevToolsEndpoint.WaitForActivePortAsync(
            _profile, TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(5), CancellationToken.None);

        // The first read runs before the call returns, and finds nothing.
        Assert.False(wait.IsCompleted);
        WritePortFile(UsablePortFile);
        Assert.Equal(4242, await wait.WaitAsync(TestTimeout));
    }

    [Theory]
    [InlineData("")]
    // Partially written: the browser has not finished the first line yet.
    [InlineData("93")]
    [InlineData("9333")]
    // A finished first line that is not a port.
    [InlineData("\n/devtools/browser/b")]
    [InlineData("abc\n/devtools/browser/b")]
    [InlineData("0\n/devtools/browser/b")]
    [InlineData("65536\n/devtools/browser/b")]
    [InlineData("-1\n/devtools/browser/b")]
    [InlineData("+9333\n/devtools/browser/b")]
    [InlineData(" 9333\n/devtools/browser/b")]
    [InlineData("9333 \n/devtools/browser/b")]
    [InlineData("99999999999\n/devtools/browser/b")]
    public async Task WaitForActivePortAsync_UnusableContents_KeepsPolling(string unusable)
    {
        var reads = new List<string>();

        var port = await DevToolsEndpoint.WaitForActivePortAsync(
                _profile, TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(5), path =>
                {
                    reads.Add(path);
                    return reads.Count == 1 ? unusable : UsablePortFile;
                }, CancellationToken.None)
            .WaitAsync(TestTimeout);

        Assert.Equal(4242, port);
        Assert.Equal([PortFile, PortFile], reads);
    }

    [Fact]
    public async Task WaitForActivePortAsync_FileMissingOrStillHeld_KeepsPolling()
    {
        // The browser may still be creating or writing the file; Windows reports a held file either way.
        var reads = 0;

        var port = await DevToolsEndpoint.WaitForActivePortAsync(
                _profile, TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(5), _ => ++reads switch
                {
                    1 => throw new FileNotFoundException("missing"),
                    2 => throw new DirectoryNotFoundException("missing"),
                    3 => throw new IOException("held"),
                    4 => throw new UnauthorizedAccessException("denied"),
                    _ => UsablePortFile,
                }, CancellationToken.None)
            .WaitAsync(TestTimeout);

        Assert.Equal(4242, port);
        Assert.Equal(5, reads);
    }

    [Fact]
    public async Task WaitForActivePortAsync_UnexpectedReadFailure_Propagates()
    {
        var reads = 0;
        var failure = new NotSupportedException("bad path");

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => DevToolsEndpoint.WaitForActivePortAsync(
                _profile, TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(5), _ =>
                {
                    reads++;
                    throw failure;
                }, CancellationToken.None)
            .WaitAsync(TestTimeout));

        Assert.Same(failure, ex);
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task WaitForActivePortAsync_NoPortWithinTimeout_ThrowsTimeoutWithoutTheFileContents()
    {
        WritePortFile("SECRET");

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => DevToolsEndpoint.WaitForActivePortAsync(
                _profile, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10), CancellationToken.None)
            .WaitAsync(TestTimeout));

        Assert.Equal("The browser did not report its debugging port within 1 seconds.", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task WaitForActivePortAsync_AlreadyCancelled_ThrowsOperationCanceledEvenWithThePortWritten()
    {
        WritePortFile(UsablePortFile);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DevToolsEndpoint.WaitForActivePortAsync(
            _profile, TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(5), cts.Token).WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task WaitForActivePortAsync_CallerCancelsWhileWaiting_ThrowsOperationCanceledNotTimeout()
    {
        using var cts = new CancellationTokenSource();
        var wait = DevToolsEndpoint.WaitForActivePortAsync(
            _profile, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(10), cts.Token);
        Assert.False(wait.IsCompleted);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait.WaitAsync(TestTimeout));
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body)
        };

    private void WritePortFile(string contents)
    {
        Directory.CreateDirectory(_profile);
        File.WriteAllText(PortFile, contents);
    }

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
    // Addresses that are not an absolute ws:// address on the loopback host at the port the browser reported.
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
    public async Task WaitForPageAsync_AcceptsLoopbackAddressesAtTheReportedPort(string address)
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
