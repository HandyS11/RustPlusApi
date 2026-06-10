using RustPlusApi.MockServer;
using RustPlusContracts;
using System.Net.WebSockets;
using Xunit;

namespace RustPlusApi.Tests.Integration;

/// <summary>
/// Covers the error-raising paths of the socket: failed connect, abnormal receive, and
/// fail-fast of in-flight requests when the transport dies.
/// </summary>
public class SocketErrorTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task TransportDeath_FailsPendingRequestsImmediately()
    {
        // The responder never replies, so the request would otherwise sit until its full request
        // timeout (default 30 s). Killing the transport must fault it right away instead.
        var server = new MockRustPlusServer(_ => null);
        server.Start();
        await using var client = new RustPlus(MockRustPlusServer.Host, server.Port, 1, 1);
        await client.ConnectAsync().WaitAsync(Timeout);

        var requestTask = client.SendRequestAsync(new AppRequest { GetInfo = new AppEmpty() });
        await Task.Delay(150); // request is in flight
        Assert.Equal(1, client.PendingRequestCountForTests);

        await server.DisposeAsync(); // tears the socket out from under the client

        // Well under the 30 s request timeout: the receive loop's fail-fast must fault the request.
        await Assert.ThrowsAsync<WebSocketException>(() => requestTask.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, client.PendingRequestCountForTests);
    }

    [Fact]
    public async Task ForceClose_FailsUnansweredRequestsInsteadOfTimingOut()
    {
        // A graceful-but-forced disconnect closes the socket while a request is still unanswered.
        // No response can arrive after the close, so the request must fail fast, not time out.
        await using var server = new MockRustPlusServer(_ => null);
        server.Start();
        await using var client = new RustPlus(MockRustPlusServer.Host, server.Port, 1, 1);
        await client.ConnectAsync().WaitAsync(Timeout);

        var requestTask = client.SendRequestAsync(new AppRequest { GetInfo = new AppEmpty() });
        await Task.Delay(150);

        await client.DisconnectAsync(forceClose: true).WaitAsync(Timeout);

        await Assert.ThrowsAsync<WebSocketException>(() => requestTask.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task CustomRequestTimeout_IsHonored()
    {
        // Options wiring: a 500 ms request timeout against a server that never replies must throw
        // TimeoutException far sooner than the 30 s default.
        await using var server = new MockRustPlusServer(_ => null);
        server.Start();
        var options = new RustPlusSocketOptions { RequestTimeout = TimeSpan.FromMilliseconds(500) };
        await using var client = new RustPlus(MockRustPlusServer.Host, server.Port, 1, 1, options: options);
        await client.ConnectAsync().WaitAsync(Timeout);

        var ex = await Assert.ThrowsAsync<TimeoutException>(
            () => client.GetInfoAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Contains("0.5s", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectAsync_ToDeadPort_RaisesErrorOccurredAndThrows()
    {
        // Port 1 (or any closed loopback port) makes the WebSocket connect throw -> ErrorOccurred + rethrow.
        await using var client = new RustPlus("127.0.0.1", 1, 1, 1);
        var error = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ErrorOccurred += (_, ex) => error.TrySetResult(ex);

        // The failure must surface to the caller, not just on the event: awaiting ConnectAsync against
        // a server that was never reached can no longer look like success.
        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync().WaitAsync(Timeout));

        var ex = await error.Task.WaitAsync(Timeout);
        Assert.Same(ex, thrown);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task AbruptServerDispose_RaisesErrorOrDisconnect()
    {
        var server = new MockRustPlusServer();
        server.Start();
        await using var client = new RustPlus(MockRustPlusServer.Host, server.Port, 1, 1);
        var signalled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ErrorOccurred += (_, _) => signalled.TrySetResult(true);

        await client.ConnectAsync().WaitAsync(Timeout);
        // Round-trip so the server registers the active socket before we tear it down.
        await client.GetInfoAsync().WaitAsync(Timeout);

        await server.DisposeAsync(); // tears the socket out from under the receive loop

        // Either ErrorOccurred fires or the loop exits cleanly; assert no hang within timeout.
        var fired = await Task.WhenAny(signalled.Task, Task.Delay(2000)) == signalled.Task;
        Assert.True(fired || !client.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_WithFacepunchProxy_RaisesErrorOccurredAndThrows()
    {
        // useFacepunchProxy=true exercises the wss:// URL branch in ConnectAsync; connecting
        // to the Facepunch host will fail in CI, triggering ErrorOccurred + rethrow.
        await using var client = new RustPlus("example.invalid", 28083, 1, 1, useFacepunchProxy: true);
        var error = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ErrorOccurred += (_, ex) => error.TrySetResult(ex);

        await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync().WaitAsync(Timeout));

        var ex = await error.Task.WaitAsync(Timeout);
        Assert.NotNull(ex);
        Assert.False(client.IsConnected);
    }
}
