using RustPlusApi.MockServer;
using Xunit;

namespace RustPlusApi.Tests.Integration;

/// <summary>
/// Drives the error arm of <c>ProcessRequestAsync</c> and the early-return-on-failure
/// branches of Strobe/Toggle.
/// </summary>
public class ClientErrorPathTests
{
    private const ulong PlayerId = 76561198000000000;
    private const int PlayerToken = 123456789;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static async Task<(MockRustPlusServer, RustPlus)> ConnectErroringAsync()
    {
        var server = new MockRustPlusServer(req => MockResponses.Error(req.Seq, "no_permission"));
        server.Start();
        var client = new RustPlus(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken);
        await client.ConnectAsync().WaitAsync(Timeout);
        return (server, client);
    }

    [Fact]
    public async Task GetClanInfoAsync_OnError_SurfacesFailureMessage()
    {
        var (server, client) = await ConnectErroringAsync();
        await using var _ = server;
        using var __ = client;
        var response = await client.GetClanInfoAsync().WaitAsync(Timeout);
        Assert.False(response.IsSuccess);
        Assert.Equal("no_permission", response.Error!.Message);
    }

    [Fact]
    public async Task ToggleSmartSwitchAsync_WhenReadFails_ReturnsFailureWithoutWriting()
    {
        var (server, client) = await ConnectErroringAsync();
        await using var _ = server;
        using var __ = client;
        var response = await client.ToggleSmartSwitchAsync(1).WaitAsync(Timeout);
        Assert.False(response.IsSuccess);
    }

    [Fact]
    public async Task StrobeSmartSwitchAsync_WhenFirstSetFails_ReturnsFailure()
    {
        var (server, client) = await ConnectErroringAsync();
        await using var _ = server;
        using var __ = client;
        var response = await client.StrobeSmartSwitchAsync(1, timeoutMilliseconds: 10).WaitAsync(Timeout);
        Assert.False(response.IsSuccess);
    }
}
