using RustPlusApi.MockServer;
using RustPlusContracts;
using Xunit;

namespace RustPlusApi.Tests.Integration;

/// <summary>
/// End-to-end coverage for the entity client surface (get/set/strobe/toggle)
/// against a tailored mock responder.
/// </summary>
public class EntityClientTests
{
    private const ulong PlayerId = 76561198000000000;
    private const int PlayerToken = 123456789;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Switch state toggles per SetEntityValue so Toggle/Strobe observe a real flip.</summary>
    /// <param name="request">The incoming request to respond to.</param>
    private static AppMessage? EntityResponder(AppRequest request)
    {
        var response = new AppResponse { Seq = request.Seq };
        if (request.GetEntityInfo is not null)
        {
            response.EntityInfo = MockResponses.SampleSmartSwitch(value: true);
            return new AppMessage { Response = response };
        }
        if (request.SetEntityValue is not null)
        {
            // SetSmartSwitchValueAsync selects r.Broadcast.EntityChanged.
            return new AppMessage
            {
                Broadcast = MockResponses.SmartSwitchBroadcast((uint)request.EntityId, request.SetEntityValue.Value)
            };
        }
        if (request.CheckSubscription is not null)
        {
            response.Flag = new AppFlag { Value = true };
            return new AppMessage { Response = response };
        }
        if (request.SendTeamMessage is not null)
        {
            // SendTeamMessageAsync selects r.Broadcast.TeamMessage.Message.ToTeamMessage()
            return new AppMessage { Broadcast = MockResponses.TeamMessageSendBroadcast(request.PlayerId, request.SendTeamMessage.Message) };
        }
        response.Success = new AppSuccess();
        return new AppMessage { Response = response };
    }

    private static async Task<(MockRustPlusServer, RustPlus)> ConnectEntityAsync()
    {
        var server = new MockRustPlusServer(EntityResponder);
        server.Start();
        var client = new RustPlus(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken);
        await client.ConnectAsync().WaitAsync(Timeout);
        return (server, client);
    }

    private static async Task<(MockRustPlusServer, RustPlus)> ConnectDefaultAsync()
    {
        var server = new MockRustPlusServer();
        server.Start();
        var client = new RustPlus(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken);
        await client.ConnectAsync().WaitAsync(Timeout);
        return (server, client);
    }

    [Fact]
    public async Task GetSmartSwitchInfoAsync_ReturnsActiveState()
    {
        var (server, client) = await ConnectEntityAsync();
        await using var _ = server;
        await using var __ = client;
        var response = await client.GetSmartSwitchInfoAsync(1).WaitAsync(Timeout);
        Assert.True(response.IsSuccess);
        Assert.True(response.Data!.IsActive);
    }

    [Fact]
    public async Task GetAlarmInfoAsync_ReturnsAlarmInfo()
    {
        await using var server = new MockRustPlusServer(req =>
        {
            var resp = new AppResponse { Seq = req.Seq };
            if (req.GetEntityInfo is not null)
            {
                resp.EntityInfo = MockResponses.SampleAlarm(value: false);
            }
            else
            {
                resp.Success = new AppSuccess();
            }

            return new AppMessage { Response = resp };
        });
        server.Start();
        await using var client = new RustPlus(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken);
        await client.ConnectAsync().WaitAsync(Timeout);

        var response = await client.GetAlarmInfoAsync(1).WaitAsync(Timeout);
        Assert.True(response.IsSuccess);
        Assert.False(response.Data!.IsActive);
    }

    [Fact]
    public async Task GetStorageMonitorInfoAsync_ReturnsStorageMonitorInfo()
    {
        await using var server = new MockRustPlusServer(req =>
        {
            var resp = new AppResponse { Seq = req.Seq };
            if (req.GetEntityInfo is not null)
            {
                resp.EntityInfo = MockResponses.SampleStorageMonitor();
            }
            else
            {
                resp.Success = new AppSuccess();
            }

            return new AppMessage { Response = resp };
        });
        server.Start();
        await using var client = new RustPlus(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken);
        await client.ConnectAsync().WaitAsync(Timeout);

        var response = await client.GetStorageMonitorInfoAsync(1).WaitAsync(Timeout);
        Assert.True(response.IsSuccess);
        Assert.Equal(48, response.Data!.Capacity);
        Assert.Single(response.Data.Items!);
    }

    [Fact]
    public async Task CheckSubscriptionAsync_ReturnsFlag()
    {
        var (server, client) = await ConnectEntityAsync();
        await using var _ = server;
        await using var __ = client;
        var response = await client.CheckSubscriptionAsync(1).WaitAsync(Timeout);
        Assert.True(response.IsSuccess);
        Assert.True(response.Data!.IsSubscribed);
    }

    [Fact]
    public async Task SetSmartSwitchValueAsync_ReturnsUpdatedState()
    {
        var (server, client) = await ConnectEntityAsync();
        await using var _ = server;
        await using var __ = client;
        var response = await client.SetSmartSwitchValueAsync(1, true).WaitAsync(Timeout);
        Assert.True(response.IsSuccess);
        Assert.True(response.Data!.IsActive);
    }

    [Fact]
    public async Task ToggleSmartSwitchAsync_ReadsThenWritesNegation()
    {
        var (server, client) = await ConnectEntityAsync();
        await using var _ = server;
        await using var __ = client;
        // Current state is true (GetEntityInfo returns true), so Toggle writes false.
        var response = await client.ToggleSmartSwitchAsync(1).WaitAsync(Timeout);
        Assert.True(response.IsSuccess);
        Assert.False(response.Data!.IsActive);
    }

    [Fact]
    public async Task StrobeSmartSwitchAsync_CompletesBothFlips()
    {
        var (server, client) = await ConnectEntityAsync();
        await using var _ = server;
        await using var __ = client;
        var response = await client.StrobeSmartSwitchAsync(1, timeoutMilliseconds: 10, value: true)
            .WaitAsync(Timeout);
        Assert.True(response.IsSuccess);
    }

    [Fact]
    public async Task SetSubscriptionAsync_ReportsSuccess()
    {
        var (server, client) = await ConnectEntityAsync();
        await using var _ = server;
        await using var __ = client;
        var response = await client.SetSubscriptionAsync(1).WaitAsync(Timeout);
        Assert.True(response.IsSuccess);
    }

    [Fact]
    public async Task SendTeamMessageAsync_ReturnsMappedMessage()
    {
        var (server, client) = await ConnectEntityAsync();
        await using var _ = server;
        await using var __ = client;
        var response = await client.SendTeamMessageAsync("hi").WaitAsync(Timeout);
        Assert.True(response.IsSuccess);
        Assert.Equal("hi", response.Data!.Message);
    }

    [Fact]
    public async Task GetMapAsync_ReturnsMappedServerMap()
    {
        var (server, client) = await ConnectDefaultAsync();
        await using var _ = server;
        await using var __ = client;
        var response = await client.GetMapAsync().WaitAsync(Timeout);
        Assert.True(response.IsSuccess);
        Assert.Equal(2000u, response.Data!.Width);
    }

    [Fact]
    public async Task GetMapMarkersAsync_ReturnsMappedMarkers()
    {
        var (server, client) = await ConnectDefaultAsync();
        await using var _ = server;
        await using var __ = client;
        var response = await client.GetMapMarkersAsync().WaitAsync(Timeout);
        Assert.True(response.IsSuccess);
        Assert.NotNull(response.Data);
        Assert.True(response.Data!.PlayerMarkers.ContainsKey(1));
    }

    [Fact]
    public async Task GetTeamInfoAsync_ReturnsMappedTeamInfo()
    {
        var (server, client) = await ConnectDefaultAsync();
        await using var _ = server;
        await using var __ = client;
        var response = await client.GetTeamInfoAsync().WaitAsync(Timeout);
        Assert.True(response.IsSuccess);
        Assert.Equal(76561198000000001ul, response.Data!.LeaderSteamId);
        Assert.Single(response.Data.Members!);
    }

    [Fact]
    public async Task GetTeamChatAsync_ReturnsMappedMessages()
    {
        var (server, client) = await ConnectDefaultAsync();
        await using var _ = server;
        await using var __ = client;
        var response = await client.GetTeamChatAsync().WaitAsync(Timeout);
        Assert.True(response.IsSuccess);
        var message = Assert.Single(response.Data!.Messages!);
        Assert.Equal("team chat fixture", message.Message);
    }
}
