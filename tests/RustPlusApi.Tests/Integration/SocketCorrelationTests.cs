using RustPlusApi.MockServer;
using RustPlusContracts;
using Xunit;

namespace RustPlusApi.Tests.Integration;

/// <summary>
/// Phase 2 request/response correlation: responses are matched to their request by sequence number,
/// so an unsolicited broadcast cannot consume a pending request's slot, and a request honors a
/// cancellation token / removes its pending entry when cancelled.
/// </summary>
public class SocketCorrelationTests
{
    private const ulong PlayerId = 76561198000000000;
    private const int PlayerToken = 123456789;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task BroadcastDuringPendingRequest_DoesNotStealResponse()
    {
        // The responder parks on the gate for GetInfo, so the request stays pending while we push a
        // broadcast. Under positional queue coupling the broadcast would resolve the pending request
        // with the wrong message; correlation by Seq must keep the broadcast and the response separate.
        var gate = new SemaphoreSlim(0, 1);
        await using var server = new MockRustPlusServer(req =>
        {
            if (req.GetInfo is not null)
            {
                gate.Wait(TimeSpan.FromSeconds(5));
            }
            return MockResponses.Default(req);
        });
        server.Start();
        await using var client = new RustPlus(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken);
        await client.ConnectAsync().WaitAsync(Timeout);

        var requestTask = client.GetInfoAsync();
        await Task.Delay(150); // request has reached the server; the responder is parked on the gate

        // Broadcast arrives while the request is still pending.
        await server.BroadcastAsync(MockResponses.TeamMessageBroadcast(1, "x", "hi"));
        await Task.Delay(50); // let the client process the broadcast

        gate.Release(); // now the real response is sent

        var response = await requestTask.WaitAsync(Timeout);

        Assert.True(response.IsSuccess);
        Assert.NotNull(response.Data);
        Assert.Equal("Mock Rust Server", response.Data!.Name);
    }

    [Fact]
    public async Task SendRequestAsync_WhenTokenCancelled_ThrowsAndRemovesPendingEntry()
    {
        // The server never replies, so the request stays pending until the caller's token cancels it.
        await using var server = new MockRustPlusServer(_ => null);
        server.Start();
        await using var client = new RustPlus(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken);
        await client.ConnectAsync().WaitAsync(Timeout);

        using var cts = new CancellationTokenSource();
        var task = client.SendRequestAsync(new AppRequest { GetInfo = new AppEmpty() }, cancellationToken: cts.Token);

        await Task.Delay(150);
        Assert.Equal(1, client.PendingRequestCountForTests); // request is in flight

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(0, client.PendingRequestCountForTests); // pending entry removed on cancellation
    }
}
