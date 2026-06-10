using RustPlusApi.Data.Events;
using RustPlusApi.MockServer;
using Xunit;

namespace RustPlusApi.IntegrationTests;

/// <summary>
/// End-to-end tests that point a real <see cref="RustPlus"/> client at the in-process
/// <see cref="MockRustPlusServer"/>. This is the harness the riskier phases
/// (serializer swap, camera) build on.
/// </summary>
public class RustPlusClientTests
{
    private const ulong PlayerId = 76561198000000000;
    private const int PlayerToken = 123456789;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task GetInfoAsync_ReturnsMappedServerInfo()
    {
        await using var server = new MockRustPlusServer();
        server.Start();
        await using var client = new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));
        await client.ConnectAsync().WaitAsync(Timeout);

        var response = await client.GetInfoAsync().WaitAsync(Timeout);

        Assert.True(response.IsSuccess);
        Assert.NotNull(response.Data);
        Assert.Equal("Mock Rust Server", response.Data!.Name);
        Assert.Equal(4000u, response.Data.MapSize);
        Assert.Equal(42u, response.Data.PlayerCount);
    }

    [Fact]
    public async Task GetTimeAsync_ReturnsMappedTimeInfo()
    {
        await using var server = new MockRustPlusServer();
        server.Start();
        await using var client = new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));
        await client.ConnectAsync().WaitAsync(Timeout);

        var response = await client.GetTimeAsync().WaitAsync(Timeout);

        Assert.True(response.IsSuccess);
        Assert.NotNull(response.Data);
        Assert.Equal(12.5f, response.Data!.Time);
        Assert.Equal(60f, response.Data.DayLengthMinutes);
    }

    [Fact]
    public async Task GetInfoAsync_WhenServerReturnsError_SurfacesFailure()
    {
        await using var server = new MockRustPlusServer(
            request => MockResponses.Error(request.Seq, "not_found"));
        server.Start();
        await using var client = new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));
        await client.ConnectAsync().WaitAsync(Timeout);

        var response = await client.GetInfoAsync().WaitAsync(Timeout);

        Assert.False(response.IsSuccess);
        Assert.NotNull(response.Error);
        Assert.Equal("not_found", response.Error!.Message);
    }

    [Fact]
    public async Task TeamChatBroadcast_RaisesOnTeamChatReceived()
    {
        await using var server = new MockRustPlusServer();
        server.Start();
        await using var client = new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));

        var received = new TaskCompletionSource<TeamMessageEventArg>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnTeamChatReceived += (_, e) => received.TrySetResult(e);

        await client.ConnectAsync().WaitAsync(Timeout);

        // Round-trip first so the server has registered the active socket.
        await client.GetInfoAsync().WaitAsync(Timeout);

        await server.BroadcastAsync(
            MockResponses.TeamMessageBroadcast(PlayerId, "Tester", "hello from the mock"));

        var message = await received.Task.WaitAsync(Timeout);

        Assert.Equal("Tester", message.Name);
        Assert.Equal("hello from the mock", message.Message);
    }
}
