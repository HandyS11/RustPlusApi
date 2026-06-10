using ProtoBuf;
using RustPlusApi.Data.Events;
using RustPlusApi.MockServer;
using RustPlusContracts;
using System.Net.WebSockets;
using Xunit;

namespace RustPlusApi.IntegrationTests;

/// <summary>
/// Covers socket lifecycle events, SetPlayer, disconnect variants, and the
/// storage-monitor + unknown broadcast arms of ParseNotification.
/// </summary>
public class SocketLifecycleTests
{
    private const ulong PlayerId = 76561198000000000;
    private const int PlayerToken = 123456789;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ConnectAndRequest_RaisesLifecycleEventsInOrder()
    {
        await using var server = new MockRustPlusServer();
        server.Start();
        await using var client = new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));

        var events = new List<string>();
        client.Connecting += (_, _) => events.Add("connecting");
        client.Connected += (_, _) => events.Add("connected");
        client.SendingRequest += (_, _) => events.Add("sending");
        client.RequestSent += (_, _) => events.Add("sent");
        client.MessageReceived += (_, _) => events.Add("message");
        client.ResponseReceived += (_, _) => events.Add("response");

        await client.ConnectAsync().WaitAsync(Timeout);
        await client.GetInfoAsync().WaitAsync(Timeout);

        Assert.Equal("connecting", events[0]);
        Assert.Equal("connected", events[1]);
        Assert.Contains("sending", events);
        Assert.Contains("sent", events);
        Assert.Contains("message", events);
        Assert.Contains("response", events);
    }

    [Fact]
    public async Task SetPlayer_ChangesCredentialsOnNextRequest()
    {
        ulong? observedId = null;
        await using var server = new MockRustPlusServer(req =>
        {
            observedId = req.PlayerId;
            return MockResponses.Default(req);
        });
        server.Start();
        await using var client = new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));
        await client.ConnectAsync().WaitAsync(Timeout);

        client.SetPlayer(42, 7);
        await client.GetTimeAsync().WaitAsync(Timeout);

        Assert.Equal(42ul, observedId);
    }

    [Fact]
    public void IsConnected_BeforeConnect_IsFalse()
    {
        using var client = new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, 1, PlayerId, PlayerToken));
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task DisconnectAsync_WhenNotConnected_ReturnsImmediately()
    {
        await using var client = new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, 1, PlayerId, PlayerToken));
        await client.DisconnectAsync().WaitAsync(Timeout); // early return, no throw
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task DisconnectAsync_ForceClose_RaisesDisconnectingAndDisconnected()
    {
        await using var server = new MockRustPlusServer();
        server.Start();
        await using var client = new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));
        await client.ConnectAsync().WaitAsync(Timeout);

        var disconnecting = false;
        var disconnected = false;
        client.Disconnecting += (_, _) => disconnecting = true;
        client.Disconnected += (_, _) => disconnected = true;

        await client.DisconnectAsync(forceClose: true).WaitAsync(Timeout);

        Assert.True(disconnecting);
        Assert.True(disconnected);
    }

    [Fact]
    public async Task ConnectAsync_AfterDisconnect_ReconnectsAndServesRequests()
    {
        await using var server = new MockRustPlusServer();
        server.Start();
        await using var client = new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));

        await client.ConnectAsync().WaitAsync(Timeout);
        var first = await client.GetInfoAsync().WaitAsync(Timeout);
        Assert.True(first.IsSuccess);

        await client.DisconnectAsync().WaitAsync(Timeout);
        Assert.False(client.IsConnected);

        // Same instance reconnects: the previous socket is released and a fresh one serves requests.
        await client.ConnectAsync().WaitAsync(Timeout);
        Assert.True(client.IsConnected);

        var second = await client.GetInfoAsync().WaitAsync(Timeout);
        Assert.True(second.IsSuccess);
        Assert.Equal("Mock Rust Server", second.Data!.Name);
    }

    [Fact]
    public async Task ConnectAsync_WhenAlreadyConnected_ThrowsInvalidOperation()
    {
        await using var server = new MockRustPlusServer();
        server.Start();
        await using var client = new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));
        await client.ConnectAsync().WaitAsync(Timeout);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync().WaitAsync(Timeout));
        Assert.True(client.IsConnected); // the live connection is untouched
    }

    [Fact]
    public async Task ConnectAsync_AfterDispose_ThrowsObjectDisposed()
    {
        var client = new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, 1, PlayerId, PlayerToken));
        await client.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConnectAsync());
    }

    [Fact]
    public async Task StorageMonitorBroadcast_RaisesOnStorageMonitorTriggered()
    {
        await using var server = new MockRustPlusServer();
        server.Start();
        await using var client = new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));
        var received = new TaskCompletionSource<StorageMonitorEventArg>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnStorageMonitorTriggered += (_, e) => received.TrySetResult(e);

        await client.ConnectAsync().WaitAsync(Timeout);
        // Round-trip first so the server has registered the active socket.
        await client.GetInfoAsync().WaitAsync(Timeout);

        // Capacity != 0 routes to the storage-monitor arm in ParseNotification.
        await server.BroadcastAsync(new AppBroadcast
        {
            EntityChanged = new AppEntityChanged
            {
                EntityId = 5,
                Payload = new AppEntityPayload { Capacity = 24, Value = false }
            }
        });

        var ev = await received.Task.WaitAsync(Timeout);
        Assert.Equal(5u, ev.Id);
        Assert.Equal(24, ev.Capacity);
    }

    [Fact]
    public async Task UnknownBroadcast_IsIgnoredWithoutThrowing()
    {
        await using var server = new MockRustPlusServer();
        server.Start();
        await using var client = new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));
        await client.ConnectAsync().WaitAsync(Timeout);
        // Round-trip so server registers the active socket.
        await client.GetInfoAsync().WaitAsync(Timeout);

        // A broadcast with no recognized payload exercises the Debug.WriteLine fall-through in ParseNotification.
        await server.BroadcastAsync(new AppBroadcast());
        await Task.Delay(200);

        Assert.True(client.IsConnected); // still alive, nothing thrown
    }

    [Fact]
    public async Task Dispose_WhileConnected_CancelsReceiveLoop()
    {
        await using var server = new MockRustPlusServer();
        server.Start();
        var client = new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));
        await client.ConnectAsync().WaitAsync(Timeout);
        await client.GetInfoAsync().WaitAsync(Timeout);

        Assert.True(client.IsConnected);

        // Intentionally exercises the synchronous Dispose path (kept for back-compat alongside DisposeAsync).
#pragma warning disable CA1849, VSTHRD103, S6966 // sync Dispose is the behavior under test here
        client.Dispose();
#pragma warning restore CA1849, VSTHRD103, S6966

        await Task.Delay(300);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task DisposeAsync_WhenClientNeverAcksClose_CompletesWithoutHanging()
    {
        // Block the responder so the server's read loop is parked inside _responder (not in a
        // cancellable ReceiveAsync). The active socket therefore stays Open through dispose,
        // forcing the graceful-close path against a peer that will never ack the handshake.
        var gate = new SemaphoreSlim(0, 1);
        var server = new MockRustPlusServer(req =>
        {
            gate.Wait();
            return MockResponses.Default(req);
        });
        server.Start();

        try
        {
            using var peer = new ClientWebSocket();
            await peer.ConnectAsync(new Uri($"ws://{MockRustPlusServer.Host}:{server.Port}"), CancellationToken.None)
                .WaitAsync(Timeout);

            await using var request = new MemoryStream();
            Serializer.Serialize(request, new AppRequest
            {
                Seq = 1,
                PlayerId = PlayerId,
                PlayerToken = PlayerToken,
                GetInfo = new AppEmpty()
            });
            await peer.SendAsync(request.ToArray(), WebSocketMessageType.Binary, true, CancellationToken.None)
                .WaitAsync(Timeout);

            // Give the server time to receive the request and park in the blocked responder.
            await Task.Delay(300);

            // Disposing must not block waiting for a close acknowledgement that never arrives.
            var dispose = server.DisposeAsync().AsTask();
            await dispose.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(dispose.IsCompletedSuccessfully);
        }
        finally
        {
            gate.Release();
        }
    }

    [Fact]
    public async Task DisconnectAsync_GracefulClose_DrainsPendingResponseQueue()
    {
        var gate = new SemaphoreSlim(0, 1);
        await using var server = new MockRustPlusServer(req =>
        {
            gate.Wait(TimeSpan.FromSeconds(5));
            return MockResponses.Default(req);
        });
        server.Start();
        await using var client = new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));
        await client.ConnectAsync().WaitAsync(Timeout);

        var requestTask = client.GetInfoAsync();

        await Task.Delay(150);

        var disconnectTask = client.DisconnectAsync(forceClose: false);

        await Task.Delay(100);
        gate.Release();

        await Task.WhenAll(requestTask, disconnectTask).WaitAsync(Timeout);

        Assert.False(client.IsConnected);
    }
}
