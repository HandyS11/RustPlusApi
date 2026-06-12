using RustPlusApi.Camera;
using RustPlusApi.Data.Cameras;
using RustPlusApi.Data.Events;
using RustPlusApi.MockServer;
using RustPlusContracts;
using Xunit;

namespace RustPlusApi.IntegrationTests;

/// <summary>
/// End-to-end tests for <see cref="CameraController"/> against the mock server:
/// subscribe, keep-alive renewal, ray forwarding, the press-and-release input
/// helpers and the unsubscribe-on-dispose behaviour.
/// </summary>
public class CameraControllerTests
{
    private const ulong PlayerId = 76561198000000000;
    private const int PlayerToken = 123456789;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static async Task<RustPlus> ConnectAsync(MockRustPlusServer server)
    {
        var client =
            new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));
        await client.ConnectAsync().WaitAsync(Timeout);
        return client;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.True(condition(), "Condition was not satisfied within the timeout.");
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsControllerWithInfo()
    {
        await using var server = new MockRustPlusServer();
        server.Start();
        await using var client = await ConnectAsync(server);

        var response = await CameraController.SubscribeAsync(client, "CAM01").WaitAsync(Timeout);

        Assert.True(response.IsSuccess);
        var controller = response.Data!;
        await using var _ = controller;
        Assert.Equal("CAM01", controller.CameraId);
        Assert.Equal(640, controller.Info.Width);
    }

    [Fact]
    public async Task SubscribeAsync_ResubscribesPeriodically()
    {
        var subscribeCount = 0;
        var server = new MockRustPlusServer(request =>
        {
            if (request.CameraSubscribe is not null)
            {
                Interlocked.Increment(ref subscribeCount);
            }

            return MockResponses.Default(request);
        });
        await using var _ = server;
        server.Start();
        await using var client = await ConnectAsync(server);

        var response = await CameraController
            .SubscribeAsync(client, "CAM01", resubscribeInterval: TimeSpan.FromMilliseconds(100))
            .WaitAsync(Timeout);
        await using var controller = response.Data!;

        await WaitUntilAsync(() => Volatile.Read(ref subscribeCount) >= 3);

        Assert.True(Volatile.Read(ref subscribeCount) >= 3);
    }

    [Fact]
    public async Task OnFrameReceived_ForwardsBroadcastFrames()
    {
        await using var server = new MockRustPlusServer();
        server.Start();
        await using var client = await ConnectAsync(server);

        var response = await CameraController.SubscribeAsync(client, "CAM01").WaitAsync(Timeout);
        await using var controller = response.Data!;

        var received = new TaskCompletionSource<CameraRaysEventArg>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        controller.OnFrameReceived += (_, e) => received.TrySetResult(e);

        await server.BroadcastAsync(MockResponses.CameraRaysBroadcast());

        var frame = await received.Task.WaitAsync(Timeout);

        Assert.Equal(65f, frame.VerticalFov);
        Assert.NotEmpty(frame.RayData);
    }

    [Fact]
    public async Task ShootAsync_SendsPressThenRelease()
    {
        var buttons = new List<int>();
        var server = new MockRustPlusServer(request =>
        {
            if (request.CameraInput is not null)
            {
                lock (buttons)
                {
                    buttons.Add(request.CameraInput.Buttons);
                }
            }

            return MockResponses.Default(request);
        });
        await using var _ = server;
        server.Start();
        await using var client = await ConnectAsync(server);

        var response = await CameraController.SubscribeAsync(client, "CAM01").WaitAsync(Timeout);
        await using var controller = response.Data!;

        var shoot = await controller.ShootAsync().WaitAsync(Timeout);

        Assert.True(shoot.IsSuccess);
        lock (buttons)
        {
            Assert.Equal([(int)CameraButtons.FirePrimary, (int)CameraButtons.None], buttons);
        }
    }

    [Fact]
    public async Task ReloadAsync_SendsReloadThenRelease()
    {
        var buttons = new List<int>();
        var server = new MockRustPlusServer(request =>
        {
            if (request.CameraInput is not null)
            {
                lock (buttons)
                {
                    buttons.Add(request.CameraInput.Buttons);
                }
            }

            return MockResponses.Default(request);
        });
        await using var _ = server;
        server.Start();
        await using var client = await ConnectAsync(server);

        var response = await CameraController.SubscribeAsync(client, "CAM01").WaitAsync(Timeout);
        await using var controller = response.Data!;

        var reload = await controller.ReloadAsync().WaitAsync(Timeout);

        Assert.True(reload.IsSuccess);
        lock (buttons)
        {
            Assert.Equal([(int)CameraButtons.Reload, (int)CameraButtons.None], buttons);
        }
    }

    [Fact]
    public async Task DisposeAsync_SendsUnsubscribe_AndStopsResubscribing()
    {
        var subscribeCount = 0;
        var unsubscribeCount = 0;
        var server = new MockRustPlusServer(request =>
        {
            if (request.CameraSubscribe is not null)
            {
                Interlocked.Increment(ref subscribeCount);
            }
            else if (request.CameraUnsubscribe is not null)
            {
                Interlocked.Increment(ref unsubscribeCount);
            }

            return MockResponses.Default(request);
        });
        await using var _ = server;
        server.Start();
        await using var client = await ConnectAsync(server);

        var response = await CameraController
            .SubscribeAsync(client, "CAM01", resubscribeInterval: TimeSpan.FromMilliseconds(100))
            .WaitAsync(Timeout);
        var controller = response.Data!;

        await controller.DisposeAsync();
        await WaitUntilAsync(() => Volatile.Read(ref unsubscribeCount) >= 1);

        var snapshot = Volatile.Read(ref subscribeCount);
        await Task.Delay(400);

        Assert.Equal(snapshot, Volatile.Read(ref subscribeCount));
    }

    [Fact]
    public async Task DisposeAsync_AfterClientDisconnects_DoesNotThrow()
    {
        await using var server = new MockRustPlusServer();
        server.Start();
        var client = await ConnectAsync(server);

        var response = await CameraController
            .SubscribeAsync(client, "CAM01", resubscribeInterval: TimeSpan.FromMilliseconds(50))
            .WaitAsync(Timeout);
        var controller = response.Data!;

        await client.DisconnectAsync();

        // Let the keep-alive loop hit the throwing path several times.
        await Task.Delay(300);

        // DisposeAsync must complete without throwing even though the client is disconnected.
        await controller.DisposeAsync();

        // Second call verifies idempotence after the fault path.
        await controller.DisposeAsync();

        // If we reach this point, dispose never threw — that is the assertion.
        Assert.Equal("CAM01", controller.CameraId);
    }

    [Theory]
    // Turret-shaped flags: Mouse | Fire | Reload | Crosshair — Reload marks the turret.
    [InlineData(2 | 8 | 16 | 32, true, false)]
    // Drone-shaped flags: Movement | Mouse | SprintAndDuck | Crosshair — a drone may render
    // a crosshair too, so Crosshair must NOT count as a turret; SprintAndDuck marks the drone.
    [InlineData(1 | 2 | 4 | 32, false, true)]
    // Static CCTV: no controls at all.
    [InlineData(0, false, false)]
    public async Task DeviceKindFlags_MapToIsAutoTurretAndIsDrone(int controlFlags, bool isAutoTurret, bool isDrone)
    {
        var server = new MockRustPlusServer(request =>
        {
            var message = MockResponses.Default(request);
            if (request.CameraSubscribe is not null)
            {
                message!.Response.CameraSubscribeInfo.ControlFlags = controlFlags;
            }

            return message;
        });
        await using var _ = server;
        server.Start();
        await using var client = await ConnectAsync(server);

        var response = await CameraController.SubscribeAsync(client, "DEVICE01").WaitAsync(Timeout);
        await using var controller = response.Data!;

        Assert.Equal(isAutoTurret, controller.IsAutoTurret);
        Assert.Equal(isDrone, controller.IsDrone);
    }

    [Fact]
    public async Task SubscribeAsync_FailureResponse_ReturnsErrorWithoutController()
    {
        var server = new MockRustPlusServer(request =>
            request.CameraSubscribe is not null
                ? MockResponses.Error(request.Seq, "camera_not_found")
                : MockResponses.Default(request));
        await using var _ = server;
        server.Start();
        await using var client = await ConnectAsync(server);

        var response = await CameraController.SubscribeAsync(client, "CAM01").WaitAsync(Timeout);

        Assert.False(response.IsSuccess);
        Assert.Null(response.Data);
    }
}
