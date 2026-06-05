using RustPlusApi.Data.Events;
using RustPlusApi.MockServer;
using Xunit;

namespace RustPlusApi.Tests.Integration;

/// <summary>
/// End-to-end tests for the clan/nexus surface lifted into the typed API (v2 §5b/§5c), plus
/// the <c>IsError</c> correctness fix that makes success-only responses report success.
/// </summary>
public class ClanNexusClientTests
{
    private const ulong PlayerId = 76561198000000000;
    private const int PlayerToken = 123456789;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static async Task<(MockRustPlusServer server, RustPlus client)> ConnectAsync(
        Func<RustPlusContracts.AppRequest, RustPlusContracts.AppMessage?>? responder = null)
    {
        var server = new MockRustPlusServer(responder);
        server.Start();
        var client = new RustPlus(server.Host, server.Port, PlayerId, PlayerToken);
        await client.ConnectAsync();
        return (server, client);
    }

    [Fact]
    public async Task GetClanInfoAsync_ReturnsMappedClan()
    {
        var (server, client) = await ConnectAsync();
        await using var _ = server;
        using var __ = client;

        var response = await client.GetClanInfoAsync().WaitAsync(Timeout);

        Assert.True(response.IsSuccess);
        Assert.Equal("Mock Clan", response.Data!.Name);
        Assert.Equal(4242, response.Data.ClanId);
    }

    [Fact]
    public async Task GetClanChatAsync_ReturnsMappedMessages()
    {
        var (server, client) = await ConnectAsync();
        await using var _ = server;
        using var __ = client;

        var response = await client.GetClanChatAsync().WaitAsync(Timeout);

        Assert.True(response.IsSuccess);
        var message = Assert.Single(response.Data!.Messages!);
        Assert.Equal("clan chat fixture", message.Message);
    }

    [Fact]
    public async Task GetNexusAuthAsync_ReturnsMappedAuth()
    {
        var (server, client) = await ConnectAsync();
        await using var _ = server;
        using var __ = client;

        var response = await client.GetNexusAuthAsync("app-key").WaitAsync(Timeout);

        Assert.True(response.IsSuccess);
        Assert.Equal("mock-server-id", response.Data!.ServerId);
    }

    [Fact]
    public async Task SetClanMotdAsync_ReportsSuccess()
    {
        var (server, client) = await ConnectAsync();
        await using var _ = server;
        using var __ = client;

        var response = await client.SetClanMotdAsync("new motd").WaitAsync(Timeout);

        Assert.True(response.IsSuccess);
        Assert.True(response.Data);
    }

    [Fact]
    public async Task PromoteToLeaderAsync_WithSuccessResponse_ReportsSuccess()
    {
        // Regression guard for the IsError fix: a bare AppSuccess used to be treated as an error.
        var (server, client) = await ConnectAsync();
        await using var _ = server;
        using var __ = client;

        var response = await client.PromoteToLeaderAsync(PlayerId).WaitAsync(Timeout);

        Assert.True(response.IsSuccess);
        Assert.True(response.Data);
    }

    [Fact]
    public async Task ClanMessageBroadcast_RaisesOnClanChatReceived()
    {
        var (server, client) = await ConnectAsync();
        await using var _ = server;
        using var __ = client;

        var received = new TaskCompletionSource<ClanMessageEventArg>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnClanChatReceived += (_, e) => received.TrySetResult(e);

        await client.GetClanInfoAsync().WaitAsync(Timeout);
        await server.BroadcastAsync(
            MockResponses.ClanMessageBroadcast(4242, PlayerId, "Tester", "clan broadcast"));

        var message = await received.Task.WaitAsync(Timeout);

        Assert.Equal(4242, message.ClanId);
        Assert.Equal("clan broadcast", message.Message);
    }

    [Fact]
    public async Task ClanChangedBroadcast_RaisesOnClanChanged()
    {
        var (server, client) = await ConnectAsync();
        await using var _ = server;
        using var __ = client;

        var received = new TaskCompletionSource<ClanChangedEventArg>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnClanChanged += (_, e) => received.TrySetResult(e);

        await client.GetClanInfoAsync().WaitAsync(Timeout);
        await server.BroadcastAsync(MockResponses.ClanChangedBroadcast());

        var change = await received.Task.WaitAsync(Timeout);

        Assert.NotNull(change.ClanInfo);
        Assert.Equal("Mock Clan", change.ClanInfo!.Name);
    }
}
