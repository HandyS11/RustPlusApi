using RustPlusApi.MockServer;
using Xunit;

namespace RustPlusApi.Tests.Integration;

/// <summary>
/// Covers the error-raising paths of the socket: failed connect and abnormal receive.
/// </summary>
public class SocketErrorTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ConnectAsync_ToDeadPort_RaisesErrorOccurred()
    {
        // Port 1 (or any closed loopback port) makes the WebSocket connect throw -> ErrorOccurred.
        await using var client = new RustPlus("127.0.0.1", 1, 1, 1);
        var error = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ErrorOccurred += (_, ex) => error.TrySetResult(ex);

        await client.ConnectAsync().WaitAsync(Timeout);

        var ex = await error.Task.WaitAsync(Timeout);
        Assert.NotNull(ex);
        Assert.False(client.IsConnected());
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
        Assert.True(fired || !client.IsConnected());
    }

    [Fact]
    public async Task ConnectAsync_WithFacepunchProxy_RaisesErrorOccurred()
    {
        // useFacepunchProxy=true exercises the wss:// URL branch in ConnectAsync; connecting
        // to the Facepunch host will fail in CI, triggering the catch -> ErrorOccurred.
        await using var client = new RustPlus("example.invalid", 28083, 1, 1, useFacepunchProxy: true);
        var error = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ErrorOccurred += (_, ex) => error.TrySetResult(ex);

        await client.ConnectAsync().WaitAsync(Timeout);

        var ex = await error.Task.WaitAsync(Timeout);
        Assert.NotNull(ex);
        Assert.False(client.IsConnected());
    }
}
