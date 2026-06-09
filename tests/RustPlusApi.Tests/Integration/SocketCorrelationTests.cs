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

    [Fact]
    public async Task BroadcastReply_NonMatchingBroadcasts_StayNotificationsAndDoNotResolveRequest()
    {
        // SetEntityValue gets no seq response; its reply is the EntityChanged broadcast for *that*
        // entity. An EntityChanged for another entity, or a team message, must not be consumed as
        // the reply — they stay pure notifications.
        await using var server = new MockRustPlusServer(req =>
            req.SetEntityValue is not null ? null : MockResponses.Default(req));
        server.Start();
        await using var client = new RustPlus(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken);
        var otherSwitchEvent = new TaskCompletionSource<Data.Events.SmartSwitchEventArg>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnSmartSwitchTriggered += (_, e) => otherSwitchEvent.TrySetResult(e);

        await client.ConnectAsync().WaitAsync(Timeout);
        await client.GetInfoAsync().WaitAsync(Timeout); // round-trip so the server registers the socket

        var requestTask = client.SetSmartSwitchValueAsync(42, true);
        await Task.Delay(150); // request is in flight, waiting on its broadcast reply

        // Unrelated broadcasts arrive first: another entity, then a team message.
        await server.BroadcastAsync(MockResponses.SmartSwitchBroadcast(7, true));
        await server.BroadcastAsync(MockResponses.TeamMessageBroadcast(999, "Other", "hi"));

        var notified = await otherSwitchEvent.Task.WaitAsync(Timeout);
        Assert.Equal(7u, notified.Id); // delivered as a notification, not stolen as the reply
        Assert.False(requestTask.IsCompleted); // the request is still waiting for *its* broadcast

        // Now the genuine reply for entity 42 arrives.
        await server.BroadcastAsync(MockResponses.SmartSwitchBroadcast(42, true));

        var response = await requestTask.WaitAsync(Timeout);
        Assert.True(response.IsSuccess);
        Assert.True(response.Data!.IsActive);
    }

    [Fact]
    public async Task SendTeamMessageAsync_IgnoresAnotherPlayersMessage()
    {
        // The reply to SendTeamMessage is the broadcast echoing *our own* message. Another team
        // member's message arriving in the window must not be returned as ours.
        await using var server = new MockRustPlusServer(req =>
            req.SendTeamMessage is not null ? null : MockResponses.Default(req));
        server.Start();
        await using var client = new RustPlus(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken);

        await client.ConnectAsync().WaitAsync(Timeout);
        await client.GetInfoAsync().WaitAsync(Timeout); // round-trip so the server registers the socket

        var requestTask = client.SendTeamMessageAsync("mine");
        await Task.Delay(150);

        // Another player's message lands first.
        await server.BroadcastAsync(MockResponses.TeamMessageBroadcast(999, "Other", "their message"));
        await Task.Delay(100);
        Assert.False(requestTask.IsCompleted);

        // The echo of our own message (our Steam ID) resolves the request.
        await server.BroadcastAsync(MockResponses.TeamMessageSendBroadcast(PlayerId, "mine"));

        var response = await requestTask.WaitAsync(Timeout);
        Assert.True(response.IsSuccess);
        Assert.Equal("mine", response.Data!.Message);
    }

    [Fact]
    public async Task BroadcastReply_ThrowingMatcher_CountsAsNoMatchAndKeepsReceiveLoopAlive()
    {
        // A matcher that throws must be treated as "no match" — never resolve the request with the
        // wrong message and never kill the receive loop. The seq-bearing response still resolves it.
        var gate = new SemaphoreSlim(0, 1);
        await using var server = new MockRustPlusServer(req =>
        {
            if (req.GetTime is not null)
            {
                gate.Wait(TimeSpan.FromSeconds(5));
            }
            return MockResponses.Default(req);
        });
        server.Start();
        await using var client = new RustPlus(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken);

        await client.ConnectAsync().WaitAsync(Timeout);
        await client.GetInfoAsync().WaitAsync(Timeout); // round-trip so the server registers the socket

        var requestTask = client.SendRequestAsync(
            new AppRequest { GetTime = new AppEmpty() },
            broadcastReplyMatcher: _ => throw new InvalidOperationException("boom"));
        await Task.Delay(150);

        await server.BroadcastAsync(MockResponses.TeamMessageBroadcast(1, "x", "hi"));
        await Task.Delay(100);
        Assert.False(requestTask.IsCompleted); // the throwing matcher did not consume the broadcast

        gate.Release(); // the seq response arrives and resolves the request

        var message = await requestTask.WaitAsync(Timeout);
        Assert.NotNull(message.Response);
        Assert.True(client.IsConnected()); // the receive loop survived the throwing matcher
    }

    [Fact]
    public async Task GetInfoAsync_HonorsCallerCancellationToken()
    {
        // Proves the caller token flows the whole public path: GetInfoAsync → ProcessRequestAsync → SendRequestAsync.
        await using var server = new MockRustPlusServer(_ => null);
        server.Start();
        await using var client = new RustPlus(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken);
        await client.ConnectAsync().WaitAsync(Timeout);

        using var cts = new CancellationTokenSource();
        var task = client.GetInfoAsync(cts.Token);

        await Task.Delay(150);
        Assert.Equal(1, client.PendingRequestCountForTests);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(0, client.PendingRequestCountForTests);
    }
}
