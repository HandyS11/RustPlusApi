using RustPlusApi;
using RustPlusApi.MockServer;
using RustPlusContracts;
using System.Net.WebSockets;
using Xunit;

namespace RustPlusApi.IntegrationTests;

/// <summary>A hostile or on-path server must not be able to make the client buffer an unbounded
/// message: reassembly is capped by <see cref="RustPlusSocketOptions.MaxMessageSize"/>.</summary>
public class SocketMessageSizeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static RustPlusSocketOptions SmallCap() =>
        new()
        {
            MaxMessageSize = 1024
        };

    [Fact]
    public async Task OversizedMessage_RaisesAnErrorAndFailsPendingRequests()
    {
        await using var server = new MockRustPlusServer(_ => null);
        server.Start();
        await using var client = new RustPlus(
            new DirectConnection(MockRustPlusServer.Host, server.Port, 1, 1), SmallCap());
        var errors = new TaskCompletionSource<Exception>();
        client.ErrorOccurred += (_, ex) => errors.TrySetResult(ex);
        await client.ConnectAsync().WaitAsync(Timeout);

        var request = client.SendRequestAsync(new AppRequest
        {
            GetInfo = new AppEmpty()
        });
        await Task.Delay(150);
        await server.BroadcastRawAsync(new byte[2048]);

        var error = await errors.Task.WaitAsync(Timeout);
        Assert.IsType<WebSocketException>(error);
        await Assert.ThrowsAsync<WebSocketException>(() => request.WaitAsync(Timeout));
    }

    [Fact]
    public async Task MessageWithinTheCap_IsStillDelivered()
    {
        // The default mock GetInfo response is well under a 1 KiB cap, so a legitimate reply still
        // round-trips with the cap in force.
        await using var server = new MockRustPlusServer();
        server.Start();
        await using var client = new RustPlus(
            new DirectConnection(MockRustPlusServer.Host, server.Port, 1, 1), SmallCap());
        await client.ConnectAsync().WaitAsync(Timeout);

        var response = await client.GetInfoAsync().WaitAsync(Timeout);

        Assert.True(response.IsSuccess);
        Assert.Equal("Mock Rust Server", response.Data!.Name);
    }
}
