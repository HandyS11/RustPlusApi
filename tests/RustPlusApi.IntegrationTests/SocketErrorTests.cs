using RustPlusApi.MockServer;
using RustPlusContracts;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using Xunit;

namespace RustPlusApi.IntegrationTests;

/// <summary>
/// Covers the error-raising paths of the socket: failed connect, abnormal receive, and
/// fail-fast of in-flight requests when the transport dies.
/// </summary>
public class SocketErrorTests
{
    private const ulong PlayerId = 76561198000000000;
    private const int PlayerToken = 123456789;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task TransportDeath_FailsPendingRequestsImmediately()
    {
        // The responder never replies, so the request would otherwise sit until its full request
        // timeout (default 30 s). Killing the transport must fault it right away instead.
        var server = new MockRustPlusServer(_ => null);
        server.Start();
        await using var client = new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, 1, 1));
        await client.ConnectAsync().WaitAsync(Timeout);

        var requestTask = client.SendRequestAsync(new AppRequest
        {
            GetInfo = new AppEmpty()
        });
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
        await using var client = new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, 1, 1));
        await client.ConnectAsync().WaitAsync(Timeout);

        var requestTask = client.SendRequestAsync(new AppRequest
        {
            GetInfo = new AppEmpty()
        });
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
        var options = new RustPlusSocketOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(500)
        };
        await using var client =
            new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, 1, 1), options);
        await client.ConnectAsync().WaitAsync(Timeout);

        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            client.GetInfoAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Contains("0.5s", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectAsync_ToDeadPort_RaisesErrorOccurredAndThrows()
    {
        // Port 1 (or any closed loopback port) makes the WebSocket connect throw -> ErrorOccurred + rethrow.
        await using var client = new RustPlus(new RustPlusConnection("127.0.0.1", 1, 1, 1));
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
        await using var client = new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, 1, 1));
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
        await using var client =
            new RustPlus(new RustPlusConnection("example.invalid", 28083, 1, 1, UseFacepunchProxy: true));
        var error = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ErrorOccurred += (_, ex) => error.TrySetResult(ex);

        await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync().WaitAsync(Timeout));

        var ex = await error.Task.WaitAsync(Timeout);
        Assert.NotNull(ex);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task SendRequestAsync_NeverConnected_ThrowsInvalidOperationException()
    {
        // Fail fast with a clear error instead of queueing into a 30s TimeoutException
        // (and instead of transmitting the stale request on a later reconnect).
        await using var client =
            new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, 1, PlayerId, PlayerToken));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetInfoAsync());
        Assert.Contains("Not connected", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendRequestAsync_AfterDisconnect_ThrowsInvalidOperationException()
    {
        await using var server = new MockRustPlusServer(MockResponses.Default);
        server.Start();
        await using var client =
            new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));
        await client.ConnectAsync().WaitAsync(Timeout);
        await client.DisconnectAsync();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetInfoAsync());
        Assert.Contains("Not connected", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendRequestAsync_AfterFailedReconnect_ThrowsInvalidOperationException()
    {
        // After a failed reconnect the socket reference is gone but the client is not disposed:
        // the guard must treat this exactly like never-connected instead of queueing.
        var server = new MockRustPlusServer(MockResponses.Default);
        server.Start();
        await using var client =
            new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));
        await client.ConnectAsync().WaitAsync(Timeout);
        await client.DisconnectAsync();
        await server.DisposeAsync(); // free the endpoint so the reconnect below is refused

        await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync().WaitAsync(Timeout));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetInfoAsync());
        Assert.Contains("Not connected", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendLoop_NonWebSocketFault_RaisesErrorOccurredAndExits()
    {
        // A failed reconnect leaves _webSocket null while the send loop (started by the first
        // connect) is still draining the channel. A request entering the channel in that window
        // must surface on ErrorOccurred and exit the loop cleanly — not kill it silently.
        var server = new MockRustPlusServer(MockResponses.Default);
        server.Start();
        await using var client =
            new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));

        await client.ConnectAsync().WaitAsync(Timeout);
        await client.DisconnectAsync();
        await server.DisposeAsync(); // free the endpoint so the reconnect below is refused

        // The reconnect disposes/nulls the old socket, then fails; the send loop stays alive.
        await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync().WaitAsync(Timeout));

        // Subscribe only now, so the connect failure above cannot satisfy the assertion.
        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ErrorOccurred += (_, ex) => errorTcs.TrySetResult(ex);

        client.EnqueueRequestForTests(new AppRequest
        {
            GetInfo = new AppEmpty()
        });

        var observed = await errorTcs.Task.WaitAsync(Timeout);
        Assert.True(observed is NullReferenceException or ObjectDisposedException,
            $"Unexpected fault type: {observed.GetType()}");
        // The loop exited through the fault handler instead of dying mid-iteration unobserved.
        await client.SendLoopForTests!.WaitAsync(Timeout);
    }

    [Fact]
    public async Task ConnectAsync_AfterSendLoopFault_DoesNotReplayStaleBacklog()
    {
        // Requests still queued when the send loop faults have had their waiters failed; replaying
        // them on a later reconnect would fire stale, out-of-context requests nobody awaits. The
        // reconnect must drop that backlog before starting the new send loop.
        var received = new ConcurrentBag<AppRequest>();
        await using var server = new MockRustPlusServer(req =>
        {
            received.Add(req);
            return MockResponses.Default(req);
        });
        server.Start();
        await using var client =
            new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));
        await client.ConnectAsync().WaitAsync(Timeout);
        await client.DisconnectAsync();

        // Fault the still-running send loop: a send attempt on the closed socket kills it.
        var errorTcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ErrorOccurred += (_, ex) => errorTcs.TrySetResult(ex);
        client.EnqueueRequestForTests(new AppRequest
        {
            GetTime = new AppEmpty()
        });
        await errorTcs.Task.WaitAsync(Timeout);
        await client.SendLoopForTests!.WaitAsync(Timeout);

        // With the loop dead, this request parks in the channel as stale backlog.
        client.EnqueueRequestForTests(new AppRequest
        {
            GetTime = new AppEmpty()
        });

        await client.ConnectAsync().WaitAsync(Timeout);
        var response = await client.GetInfoAsync().WaitAsync(Timeout);

        // The channel is FIFO: had the backlog survived, the server would have seen the stale
        // GetTime before answering the GetInfo round-trip above.
        Assert.True(response.IsSuccess);
        Assert.DoesNotContain(received, static r => r.GetTime is not null);
    }
}
