using RustPlusApi.Data.Cameras;
using RustPlusApi.Data.Events;
using RustPlusApi.MockServer;
using Xunit;

namespace RustPlusApi.Tests.Integration;

/// <summary>
/// End-to-end tests for the camera protocol layer (v2 §5a) against the mock server:
/// subscribe/input/unsubscribe plus the <c>OnCameraRaysReceived</c> stream.
/// </summary>
public class CameraClientTests
{
    private const ulong PlayerId = 76561198000000000;
    private const int PlayerToken = 123456789;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task SubscribeToCameraAsync_ReturnsMappedCameraInfo()
    {
        await using var server = new MockRustPlusServer();
        server.Start();
        using var client = new RustPlus(server.Host, server.Port, PlayerId, PlayerToken);
        await client.ConnectAsync();

        var response = await client.SubscribeToCameraAsync("CAM01").WaitAsync(Timeout);

        Assert.True(response.IsSuccess);
        Assert.Equal(640, response.Data!.Width);
        Assert.True(response.Data.ControlFlags.HasFlag(CameraControlFlags.Movement));
    }

    [Fact]
    public async Task SendCameraInputAsync_AndUnsubscribe_ReportSuccess()
    {
        await using var server = new MockRustPlusServer();
        server.Start();
        using var client = new RustPlus(server.Host, server.Port, PlayerId, PlayerToken);
        await client.ConnectAsync();

        var input = await client.SendCameraInputAsync(CameraButtons.Forward | CameraButtons.FirePrimary)
            .WaitAsync(Timeout);
        var unsubscribe = await client.UnsubscribeFromCameraAsync().WaitAsync(Timeout);

        Assert.True(input.IsSuccess);
        Assert.True(unsubscribe.IsSuccess);
    }

    [Fact]
    public async Task CameraRaysBroadcast_RaisesOnCameraRaysReceived()
    {
        await using var server = new MockRustPlusServer();
        server.Start();
        using var client = new RustPlus(server.Host, server.Port, PlayerId, PlayerToken);

        var received = new TaskCompletionSource<CameraRaysEventArg>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnCameraRaysReceived += (_, e) => received.TrySetResult(e);

        await client.ConnectAsync();
        await client.SubscribeToCameraAsync("CAM01").WaitAsync(Timeout);
        await server.BroadcastAsync(MockResponses.CameraRaysBroadcast());

        var frame = await received.Task.WaitAsync(Timeout);

        Assert.Equal(65f, frame.VerticalFov);
        Assert.Single(frame.Entities);
        Assert.NotEmpty(frame.RayData);
    }
}
