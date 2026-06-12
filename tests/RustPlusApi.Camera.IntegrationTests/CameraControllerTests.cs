using RustPlusApi.Data;
using RustPlusApi.Data.Cameras;
using RustPlusApi.Data.Events;
using RustPlusApi.MockServer;
using RustPlusContracts;
using Xunit;

namespace RustPlusApi.Camera.IntegrationTests;

/// <summary>
/// End-to-end tests for <see cref="CameraController"/> against the mock server:
/// subscribe, keep-alive renewal, ray forwarding, the press-and-release input
/// helpers and the unsubscribe-on-dispose behaviour.
/// </summary>
public class CameraControllerTests
{
    private const ulong PlayerId = 76561198000000000;
    private const int PlayerToken = 123456789;

    /// <summary>Live-observed auto-turret flags (2026-06-12): Mouse | Fire | Reload | Crosshair.</summary>
    private const int TurretFlags = 2 | 8 | 16 | 32;

    /// <summary>Live-observed drone flags (2026-06-12): Movement | Mouse | SprintAndDuck.</summary>
    private const int DroneFlags = 1 | 2 | 4;

    /// <summary>Live-observed PTZ-camera flags (2026-06-12): Mouse | Fire.</summary>
    private const int PtzFlags = 2 | 8;

    /// <summary>Live-observed static-CCTV flags (2026-06-12): None.</summary>
    private const int StaticFlags = 0;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Mock server whose camera-subscribe reports <paramref name="controlFlags"/> and
    /// which records every camera input (buttons plus mouse deltas) into <paramref name="inputs"/>.</summary>
    /// <param name="controlFlags">The control flags the camera-subscribe response advertises.</param>
    /// <param name="inputs">Recorder for every camera input received, or <see langword="null"/> to skip recording.</param>
    private static MockRustPlusServer ServerWithFlags(int controlFlags,
        List<(int Buttons, float X, float Y)>? inputs = null) =>
        new(request =>
        {
            if (request.CameraInput is not null && inputs is not null)
            {
                lock (inputs)
                {
                    inputs.Add((request.CameraInput.Buttons,
                        request.CameraInput.MouseDelta.X,
                        request.CameraInput.MouseDelta.Y));
                }
            }

            var message = MockResponses.Default(request);
            if (request.CameraSubscribe is not null)
            {
                message!.Response.CameraSubscribeInfo.ControlFlags = controlFlags;
            }

            return message;
        });

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
    public async Task ShootAsync_OnAutoTurret_SendsPressThenRelease()
    {
        var inputs = new List<(int Buttons, float X, float Y)>();
        await using var server = ServerWithFlags(TurretFlags, inputs);
        server.Start();
        await using var client = await ConnectAsync(server);

        var response = await CameraController.SubscribeAsync(client, "TURRET01").WaitAsync(Timeout);
        await using var controller = response.Data!;

        var shoot = await controller.ShootAsync().WaitAsync(Timeout);

        Assert.True(shoot.IsSuccess);
        lock (inputs)
        {
            Assert.Equal([(int)CameraButtons.FirePrimary, (int)CameraButtons.None],
                inputs.Select(i => i.Buttons));
        }
    }

    [Fact]
    public async Task ReloadAsync_OnAutoTurret_SendsReloadThenRelease()
    {
        var inputs = new List<(int Buttons, float X, float Y)>();
        await using var server = ServerWithFlags(TurretFlags, inputs);
        server.Start();
        await using var client = await ConnectAsync(server);

        var response = await CameraController.SubscribeAsync(client, "TURRET01").WaitAsync(Timeout);
        await using var controller = response.Data!;

        var reload = await controller.ReloadAsync().WaitAsync(Timeout);

        Assert.True(reload.IsSuccess);
        lock (inputs)
        {
            Assert.Equal([(int)CameraButtons.Reload, (int)CameraButtons.None],
                inputs.Select(i => i.Buttons));
        }
    }

    [Fact]
    public async Task ZoomAsync_OnPtzCamera_SendsPressThenRelease()
    {
        var inputs = new List<(int Buttons, float X, float Y)>();
        await using var server = ServerWithFlags(PtzFlags, inputs);
        server.Start();
        await using var client = await ConnectAsync(server);

        var response = await CameraController.SubscribeAsync(client, "CCTV01").WaitAsync(Timeout);
        await using var controller = response.Data!;

        var zoom = await controller.ZoomAsync().WaitAsync(Timeout);

        Assert.True(zoom.IsSuccess);
        lock (inputs)
        {
            Assert.Equal([(int)CameraButtons.FirePrimary, (int)CameraButtons.None],
                inputs.Select(i => i.Buttons));
        }
    }

    [Fact]
    public async Task ZoomAsync_OnAutoTurret_RefusedWithoutSendingInput()
    {
        // Zoom shares FirePrimary with turret fire: zooming "on" a turret would shoot it.
        var inputs = new List<(int Buttons, float X, float Y)>();
        await using var server = ServerWithFlags(TurretFlags, inputs);
        server.Start();
        await using var client = await ConnectAsync(server);

        var response = await CameraController.SubscribeAsync(client, "TURRET01").WaitAsync(Timeout);
        await using var controller = response.Data!;

        var zoom = await controller.ZoomAsync().WaitAsync(Timeout);

        Assert.False(zoom.IsSuccess);
        Assert.Equal(RustPlusErrorCode.NotSupported, zoom.Error!.Code);
        lock (inputs)
        {
            Assert.Empty(inputs);
        }
    }

    [Fact]
    public async Task ShootAsync_OnPtzCamera_RefusedWithoutSendingInput()
    {
        var inputs = new List<(int Buttons, float X, float Y)>();
        await using var server = ServerWithFlags(PtzFlags, inputs);
        server.Start();
        await using var client = await ConnectAsync(server);

        var response = await CameraController.SubscribeAsync(client, "CCTV01").WaitAsync(Timeout);
        await using var controller = response.Data!;

        var shoot = await controller.ShootAsync().WaitAsync(Timeout);

        Assert.False(shoot.IsSuccess);
        Assert.Equal(RustPlusErrorCode.NotSupported, shoot.Error!.Code);
        lock (inputs)
        {
            Assert.Empty(inputs);
        }
    }

    [Fact]
    public async Task ReloadAsync_OnDrone_RefusedWithoutSendingInput()
    {
        var inputs = new List<(int Buttons, float X, float Y)>();
        await using var server = ServerWithFlags(DroneFlags, inputs);
        server.Start();
        await using var client = await ConnectAsync(server);

        var response = await CameraController.SubscribeAsync(client, "DRONE01").WaitAsync(Timeout);
        await using var controller = response.Data!;

        var reload = await controller.ReloadAsync().WaitAsync(Timeout);

        Assert.False(reload.IsSuccess);
        Assert.Equal(RustPlusErrorCode.NotSupported, reload.Error!.Code);
        lock (inputs)
        {
            Assert.Empty(inputs);
        }
    }

    [Fact]
    public async Task LookAsync_WithMouseSupport_SendsMouseDeltas()
    {
        var inputs = new List<(int Buttons, float X, float Y)>();
        await using var server = ServerWithFlags(PtzFlags, inputs);
        server.Start();
        await using var client = await ConnectAsync(server);

        var response = await CameraController.SubscribeAsync(client, "CCTV01").WaitAsync(Timeout);
        await using var controller = response.Data!;

        var look = await controller.LookAsync(5f, -2f).WaitAsync(Timeout);

        Assert.True(look.IsSuccess);
        lock (inputs)
        {
            Assert.Equal([((int)CameraButtons.None, 5f, -2f)], inputs);
        }
    }

    [Fact]
    public async Task LookAsync_OnStaticCamera_RefusedWithoutSendingInput()
    {
        var inputs = new List<(int Buttons, float X, float Y)>();
        await using var server = ServerWithFlags(StaticFlags, inputs);
        server.Start();
        await using var client = await ConnectAsync(server);

        var response = await CameraController.SubscribeAsync(client, "CAM01").WaitAsync(Timeout);
        await using var controller = response.Data!;

        var look = await controller.LookAsync(5f, 0f).WaitAsync(Timeout);

        Assert.False(look.IsSuccess);
        Assert.Equal(RustPlusErrorCode.NotSupported, look.Error!.Code);
        lock (inputs)
        {
            Assert.Empty(inputs);
        }
    }

    [Fact]
    public async Task MoveAsync_OnDrone_StreamsInputForDuration_ThenReleases()
    {
        // Live-verified (2026-06-12): drones only react to a continuous stream of input
        // frames — a single press-and-release is acked but never moves the drone.
        var inputs = new List<(int Buttons, float X, float Y)>();
        await using var server = ServerWithFlags(DroneFlags, inputs);
        server.Start();
        await using var client = await ConnectAsync(server);

        var response = await CameraController.SubscribeAsync(client, "DRONE01").WaitAsync(Timeout);
        await using var controller = response.Data!;

        var move = await controller.MoveAsync(CameraButtons.Sprint, TimeSpan.FromMilliseconds(300))
            .WaitAsync(Timeout);

        Assert.True(move.IsSuccess);
        lock (inputs)
        {
            Assert.True(inputs.Count >= 4, $"expected a streamed burst, got {inputs.Count} frames");
            Assert.All(inputs.Take(inputs.Count - 1),
                i => Assert.Equal((int)CameraButtons.Sprint, i.Buttons));
            Assert.Equal((int)CameraButtons.None, inputs[^1].Buttons);
        }
    }

    [Fact]
    public async Task MoveAsync_DefaultDuration_StreamsAndReleases()
    {
        var inputs = new List<(int Buttons, float X, float Y)>();
        await using var server = ServerWithFlags(DroneFlags, inputs);
        server.Start();
        await using var client = await ConnectAsync(server);

        var response = await CameraController.SubscribeAsync(client, "DRONE01").WaitAsync(Timeout);
        await using var controller = response.Data!;

        var move = await controller.MoveAsync(CameraButtons.Forward).WaitAsync(Timeout);

        Assert.True(move.IsSuccess);
        lock (inputs)
        {
            Assert.True(inputs.Count >= 2, $"expected streamed frames plus release, got {inputs.Count}");
            Assert.All(inputs.Take(inputs.Count - 1),
                i => Assert.Equal((int)CameraButtons.Forward, i.Buttons));
            Assert.Equal((int)CameraButtons.None, inputs[^1].Buttons);
        }
    }

    [Fact]
    public async Task MoveAsync_OnPtzCamera_RefusedWithoutSendingInput()
    {
        var inputs = new List<(int Buttons, float X, float Y)>();
        await using var server = ServerWithFlags(PtzFlags, inputs);
        server.Start();
        await using var client = await ConnectAsync(server);

        var response = await CameraController.SubscribeAsync(client, "CCTV01").WaitAsync(Timeout);
        await using var controller = response.Data!;

        var move = await controller.MoveAsync(CameraButtons.Forward).WaitAsync(Timeout);

        Assert.False(move.IsSuccess);
        Assert.Equal(RustPlusErrorCode.NotSupported, move.Error!.Code);
        lock (inputs)
        {
            Assert.Empty(inputs);
        }
    }

    [Fact]
    public async Task MoveAsync_JumpWithoutSprintAndDuck_RefusedWithoutSendingInput()
    {
        // Movement alone covers WASD; jump/duck/sprint need the SprintAndDuck flag.
        var inputs = new List<(int Buttons, float X, float Y)>();
        await using var server = ServerWithFlags(1 /* Movement only */, inputs);
        server.Start();
        await using var client = await ConnectAsync(server);

        var response = await CameraController.SubscribeAsync(client, "DEVICE01").WaitAsync(Timeout);
        await using var controller = response.Data!;

        var move = await controller.MoveAsync(CameraButtons.Jump).WaitAsync(Timeout);

        Assert.False(move.IsSuccess);
        Assert.Equal(RustPlusErrorCode.NotSupported, move.Error!.Code);
        lock (inputs)
        {
            Assert.Empty(inputs);
        }
    }

    [Fact]
    public async Task MoveAsync_WithNonMovementButtons_Throws()
    {
        await using var server = ServerWithFlags(DroneFlags);
        server.Start();
        await using var client = await ConnectAsync(server);

        var response = await CameraController.SubscribeAsync(client, "DRONE01").WaitAsync(Timeout);
        await using var controller = response.Data!;

        await Assert.ThrowsAsync<ArgumentException>(() => controller.MoveAsync(CameraButtons.FirePrimary));
    }

    [Fact]
    public async Task OnKeepAliveFailed_RaisedWhenRenewalReturnsError()
    {
        var subscribeCount = 0;
        var server = new MockRustPlusServer(request =>
        {
            if (request.CameraSubscribe is not null && Interlocked.Increment(ref subscribeCount) > 1)
            {
                // The initial subscribe succeeds; every renewal fails as if the camera was destroyed.
                return MockResponses.Error(request.Seq, "no_player");
            }

            return MockResponses.Default(request);
        });
        await using var _ = server;
        server.Start();
        await using var client = await ConnectAsync(server);

        var response = await CameraController
            .SubscribeAsync(client, "CAM01", resubscribeInterval: TimeSpan.FromMilliseconds(50))
            .WaitAsync(Timeout);
        await using var controller = response.Data!;

        var failed = new TaskCompletionSource<ErrorMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        controller.OnKeepAliveFailed += (_, error) => failed.TrySetResult(error);

        var error = await failed.Task.WaitAsync(Timeout);

        Assert.Equal(RustPlusErrorCode.NoPlayer, error.Code);
    }

    [Fact]
    public async Task OnKeepAliveFailed_RaisedWhenClientIsDisconnected()
    {
        await using var server = new MockRustPlusServer();
        server.Start();
        var client = await ConnectAsync(server);

        var response = await CameraController
            .SubscribeAsync(client, "CAM01", resubscribeInterval: TimeSpan.FromMilliseconds(50))
            .WaitAsync(Timeout);
        await using var controller = response.Data!;

        var failed = new TaskCompletionSource<ErrorMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        controller.OnKeepAliveFailed += (_, error) => failed.TrySetResult(error);

        await client.DisconnectAsync();

        var error = await failed.Task.WaitAsync(Timeout);

        Assert.Equal(RustPlusErrorCode.Unknown, error.Code);
        Assert.False(string.IsNullOrEmpty(error.Message));
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
    // Auto-turret, as observed live (TURRET01, 2026-06-12): Mouse | Fire | Reload | Crosshair —
    // Reload marks the turret.
    [InlineData(2 | 8 | 16 | 32, true, false, false, false)]
    // Drone, as observed live (DRONE01, 2026-06-12): Movement | Mouse | SprintAndDuck.
    [InlineData(1 | 2 | 4, false, true, false, false)]
    // Hypothetical crosshair-bearing drone: Crosshair must NOT count as a turret marker.
    [InlineData(1 | 2 | 4 | 32, false, true, false, false)]
    // PTZ camera, as observed live (CCTV01, 2026-06-12): Mouse | Fire (zoom) — it can look
    // around but is neither turret nor drone.
    [InlineData(2 | 8, false, false, true, false)]
    // Static CCTV, as observed live (CAM01): no controls at all.
    [InlineData(0, false, false, false, true)]
    public async Task DeviceKindFlags_MapToExactlyOneDeviceKind(int controlFlags,
        bool isAutoTurret,
        bool isDrone,
        bool isPtzCamera,
        bool isStaticCamera)
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
        Assert.Equal(isPtzCamera, controller.IsPtzCamera);
        Assert.Equal(isStaticCamera, controller.IsStaticCamera);
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
