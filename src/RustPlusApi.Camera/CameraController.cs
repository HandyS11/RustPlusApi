using RustPlusApi.Data;
using RustPlusApi.Data.Cameras;
using RustPlusApi.Data.Events;
using RustPlusApi.Interfaces;

namespace RustPlusApi.Camera;

/// <summary>
/// Managed session for one subscribed camera: keeps the subscription alive by periodically
/// re-sending the subscribe request (the server stops streaming rays for stale subscriptions),
/// forwards ray frames via <see cref="OnFrameReceived"/>, and exposes the press-and-release
/// input helpers (<see cref="ZoomAsync"/>, <see cref="ShootAsync"/>, <see cref="ReloadAsync"/>)
/// used by PTZ cameras and auto-turrets. Mirrors the <c>Camera</c> class of
/// liamcottle/rustplus.js. Dispose to stop the keep-alive and unsubscribe.
/// </summary>
/// <remarks>The server tracks a single camera subscription per connection, so create at most
/// one live controller per <see cref="IRustPlus"/> client at a time.</remarks>
public sealed class CameraController : IAsyncDisposable
{
    /// <summary>The keep-alive renewal period used when none is supplied (mirrors rustplus.js).</summary>
    public static readonly TimeSpan DefaultResubscribeInterval = TimeSpan.FromSeconds(10);

    /// <summary>How long <see cref="MoveAsync"/> holds the buttons when no duration is supplied.
    /// 500 ms moves a live drone roughly 2 m at cruise speed.</summary>
    public static readonly TimeSpan DefaultMoveDuration = TimeSpan.FromMilliseconds(500);

    /// <summary>Cadence at which <see cref="MoveAsync"/> re-sends the held input frame —
    /// drones only actuate while receiving a continuous input stream.</summary>
    private static readonly TimeSpan MoveStreamInterval = TimeSpan.FromMilliseconds(50);

    private readonly IRustPlus _rustPlus;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _keepAlive;
    private bool _disposed;

    /// <summary>The identifier this controller is subscribed to (e.g. <c>CAM01</c>).</summary>
    public string CameraId { get; }

    /// <summary>Camera description from the most recent successful subscribe.</summary>
    public CameraInfo Info { get; private set; }

    /// <summary>Whether the camera is an auto-turret. Detected via the
    /// <see cref="CameraControlFlags.Reload"/> flag — only turrets expose a reload input.
    /// (rustplus.js checks <see cref="CameraControlFlags.Crosshair"/> instead, but a drone
    /// may also render a crosshair, so <c>Reload</c> is the safer discriminator.)</summary>
    public bool IsAutoTurret => Info.ControlFlags.HasFlag(CameraControlFlags.Reload);

    /// <summary>Whether the camera is a drone (advertises the
    /// <see cref="CameraControlFlags.SprintAndDuck"/> flag, which carries the drone's
    /// vertical movement controls).</summary>
    public bool IsDrone => Info.ControlFlags.HasFlag(CameraControlFlags.SprintAndDuck);

    /// <summary>Whether the camera is a PTZ (pan-tilt-zoom) CCTV camera: it supports
    /// <see cref="CameraControlFlags.Mouse"/> look but is neither an auto-turret nor a drone.
    /// Live-observed PTZ flags are <c>Mouse | Fire</c> — <c>Fire</c> drives
    /// <see cref="ZoomAsync"/>.</summary>
    public bool IsPtzCamera => Info.ControlFlags.HasFlag(CameraControlFlags.Mouse) && !IsAutoTurret && !IsDrone;

    /// <summary>Whether the camera is a fixed CCTV camera that accepts no input at all
    /// (<see cref="CameraInfo.ControlFlags"/> is <see cref="CameraControlFlags.None"/>).</summary>
    public bool IsStaticCamera => Info.ControlFlags == CameraControlFlags.None;

    /// <summary>Occurs when a ray frame for the subscribed camera is received.</summary>
    public event EventHandler<CameraRaysEventArg>? OnFrameReceived;

    /// <summary>Occurs when a keep-alive renewal attempt fails: the server refused the
    /// re-subscribe (e.g. <see cref="RustPlusErrorCode.NoPlayer"/> after the camera was
    /// destroyed in game) or the client was disconnected (reported as
    /// <see cref="RustPlusErrorCode.Unknown"/> with the exception message). Without a renewal
    /// the server stops streaming, so frames going quiet after this event means the
    /// subscription is dead — <see cref="Info"/> keeps its last successful value. The loop
    /// keeps retrying, so a later reconnect recovers on its own.</summary>
    public event EventHandler<ErrorMessage>? OnKeepAliveFailed;

    private CameraController(IRustPlus rustPlus, string cameraId, CameraInfo info, TimeSpan resubscribeInterval)
    {
        _rustPlus = rustPlus;
        CameraId = cameraId;
        Info = info;
        _rustPlus.OnCameraRaysReceived += ForwardFrame;
        _keepAlive = resubscribeInterval > TimeSpan.Zero
            ? KeepAliveAsync(resubscribeInterval)
            : Task.CompletedTask;
    }

    /// <summary>
    /// Subscribes to <paramref name="cameraId"/> and returns a controller that keeps the
    /// subscription alive until disposed.
    /// </summary>
    /// <param name="rustPlus">A connected client.</param>
    /// <param name="cameraId">The camera identifier configured in game (case-sensitive).</param>
    /// <param name="resubscribeInterval">How often to renew the subscription;
    /// <see cref="DefaultResubscribeInterval"/> when <see langword="null"/>,
    /// <see cref="TimeSpan.Zero"/> or negative to disable renewal.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>A <see cref="Response{T}"/> whose data is the live controller on success.</returns>
    public static async Task<Response<CameraController?>> SubscribeAsync(IRustPlus rustPlus,
        string cameraId,
        TimeSpan? resubscribeInterval = null,
        CancellationToken cancellationToken = default)
    {
        var response = await rustPlus.SubscribeToCameraAsync(cameraId, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess || response.Data is null)
        {
            return new Response<CameraController?>
            {
                IsSuccess = false, Error = response.Error
            };
        }

        return new Response<CameraController?>
        {
            IsSuccess = true,
            Data = new CameraController(rustPlus, cameraId, response.Data,
                resubscribeInterval ?? DefaultResubscribeInterval)
        };
    }

    /// <summary>
    /// Sends a single raw input frame (button bitmask plus mouse deltas) to the subscribed camera.
    /// </summary>
    /// <param name="buttons">Bitmask of buttons to report as pressed.</param>
    /// <param name="mouseDeltaX">Horizontal mouse delta.</param>
    /// <param name="mouseDeltaY">Vertical mouse delta.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>The acknowledgement <see cref="Response"/> from the server.</returns>
    public Task<Response> SendInputAsync(CameraButtons buttons,
        float mouseDeltaX = 0,
        float mouseDeltaY = 0,
        CancellationToken cancellationToken = default) =>
        _rustPlus.SendCameraInputAsync(buttons, mouseDeltaX, mouseDeltaY, cancellationToken);

    /// <summary>
    /// Performs a discrete action by pressing <paramref name="buttons"/> and then releasing them
    /// (a follow-up input with <see cref="CameraButtons.None"/>), mirroring the press-then-release
    /// pair used for PTZ and auto-turret actions in rustplus.js.
    /// </summary>
    /// <param name="buttons">Bitmask of buttons to press and then release.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>The press response if it failed; otherwise the release response.</returns>
    public async Task<Response> PressAsync(CameraButtons buttons, CancellationToken cancellationToken = default)
    {
        var press = await SendInputAsync(buttons, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!press.IsSuccess)
        {
            return press;
        }

        return await SendInputAsync(CameraButtons.None, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Advances a PTZ camera's zoom by one step (it cycles through its zoom levels and wraps),
    /// sending the <see cref="CameraButtons.FirePrimary"/> press-and-release used by rustplus.js.
    /// Refused with <see cref="RustPlusErrorCode.NotSupported"/> unless <see cref="IsPtzCamera"/>:
    /// zoom shares <see cref="CameraButtons.FirePrimary"/> with turret fire, so zooming "on" a
    /// turret would shoot it — and the server acks unsupported inputs with success, giving no
    /// feedback of its own.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>The acknowledgement <see cref="Response"/> from the server, or the
    /// client-side refusal (nothing sent).</returns>
    public Task<Response> ZoomAsync(CancellationToken cancellationToken = default) =>
        IsPtzCamera
            ? PressAsync(CameraButtons.FirePrimary, cancellationToken)
            : RefusedAsync("zoom is a PTZ-camera action (FirePrimary fires on a turret)");

    /// <summary>
    /// Fires an auto-turret once, sending the <see cref="CameraButtons.FirePrimary"/>
    /// press-and-release used by rustplus.js. Refused with
    /// <see cref="RustPlusErrorCode.NotSupported"/> unless <see cref="IsAutoTurret"/>.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>The acknowledgement <see cref="Response"/> from the server, or the
    /// client-side refusal (nothing sent).</returns>
    public Task<Response> ShootAsync(CancellationToken cancellationToken = default) =>
        IsAutoTurret
            ? PressAsync(CameraButtons.FirePrimary, cancellationToken)
            : RefusedAsync("shoot is an auto-turret action");

    /// <summary>
    /// Reloads an auto-turret, sending the <see cref="CameraButtons.Reload"/>
    /// press-and-release used by rustplus.js. Refused with
    /// <see cref="RustPlusErrorCode.NotSupported"/> unless <see cref="IsAutoTurret"/>.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>The acknowledgement <see cref="Response"/> from the server, or the
    /// client-side refusal (nothing sent).</returns>
    public Task<Response> ReloadAsync(CancellationToken cancellationToken = default) =>
        IsAutoTurret
            ? PressAsync(CameraButtons.Reload, cancellationToken)
            : RefusedAsync("reload is an auto-turret action");

    /// <summary>
    /// Turns the camera by sending a single mouse-delta frame (PTZ cameras, turrets and
    /// drones — anything advertising <see cref="CameraControlFlags.Mouse"/>). Refused with
    /// <see cref="RustPlusErrorCode.NotSupported"/> when the device does not support mouse look.
    /// </summary>
    /// <remarks>Live-observed (2026-06): actuates on PTZ cameras (the frame's
    /// <c>CameraRotation</c> pans) and on drones while airborne — a parked drone ignores the
    /// input (acked with success regardless). Turret behaviour is unverified: the test
    /// server's turret was deactivated and ignored every input.</remarks>
    /// <param name="deltaX">Horizontal mouse delta (positive looks right).</param>
    /// <param name="deltaY">Vertical mouse delta (positive looks down).</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>The acknowledgement <see cref="Response"/> from the server, or the
    /// client-side refusal (nothing sent).</returns>
    public Task<Response> LookAsync(float deltaX, float deltaY, CancellationToken cancellationToken = default) =>
        Info.ControlFlags.HasFlag(CameraControlFlags.Mouse)
            ? SendInputAsync(CameraButtons.None, deltaX, deltaY, cancellationToken)
            : RefusedAsync("this device does not support mouse look");

    /// <summary>
    /// Moves a drone by streaming the given movement buttons as input frames for
    /// <paramref name="duration"/> (default <see cref="DefaultMoveDuration"/>), then releasing.
    /// Movement only actuates while a continuous input stream is held — a single
    /// press-and-release is acknowledged by the server but never moves the drone
    /// (live-verified 2026-06). Buttons: <see cref="CameraButtons.Forward"/>/
    /// <see cref="CameraButtons.Backward"/>/<see cref="CameraButtons.Left"/>/
    /// <see cref="CameraButtons.Right"/> need <see cref="CameraControlFlags.Movement"/>;
    /// <see cref="CameraButtons.Sprint"/> (ascend) and <see cref="CameraButtons.Duck"/>
    /// (descend) need <see cref="CameraControlFlags.SprintAndDuck"/> — the drone's vertical
    /// controls, hence the flag's name (<see cref="CameraButtons.Jump"/> is accepted but did
    /// nothing on a live drone). Refused with <see cref="RustPlusErrorCode.NotSupported"/>
    /// when the device does not advertise the required flags.
    /// </summary>
    /// <remarks>Live flight 2026-06-12: streaming <c>Sprint</c> for 1 s climbed ~4.7 m,
    /// <c>Forward</c>/<c>Backward</c> moved ~5 m and back, <c>Duck</c> landed the drone on its
    /// starting spot. The drone hovers when the stream stops. A parked drone responds to
    /// <c>Sprint</c> (take-off) only — planar movement and mouse look require being airborne.
    /// Fly gently: collisions with the ground or structures (and incoming fire) cost the drone
    /// HP; a destroyed drone fails subsequent subscribes with
    /// <see cref="RustPlusErrorCode.NoPlayer"/>. A carried package (attached by a player) is
    /// dropped with a <see cref="CameraButtons.FirePrimary"/> click via
    /// <see cref="PressAsync"/>.</remarks>
    /// <param name="buttons">Movement buttons to hold.</param>
    /// <param name="duration">How long to hold the buttons; <see cref="DefaultMoveDuration"/>
    /// when <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>The release acknowledgement from the server, the first failed send, or the
    /// client-side refusal (nothing sent).</returns>
    /// <exception cref="ArgumentException"><paramref name="buttons"/> is empty or contains
    /// non-movement buttons (use the action helpers or <see cref="SendInputAsync"/> for those).</exception>
    public async Task<Response> MoveAsync(CameraButtons buttons,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        const CameraButtons planar = CameraButtons.Forward | CameraButtons.Backward
                                                           | CameraButtons.Left | CameraButtons.Right;
        const CameraButtons vertical = CameraButtons.Jump | CameraButtons.Duck | CameraButtons.Sprint;

        if (buttons == CameraButtons.None || (buttons & ~(planar | vertical)) != 0)
        {
            throw new ArgumentException(
                "MoveAsync accepts movement buttons only (Forward/Backward/Left/Right/Jump/Duck/Sprint).",
                nameof(buttons));
        }

        var required = CameraControlFlags.None;
        if ((buttons & planar) != 0)
        {
            required |= CameraControlFlags.Movement;
        }

        if ((buttons & vertical) != 0)
        {
            required |= CameraControlFlags.SprintAndDuck;
        }

        if (!Info.ControlFlags.HasFlag(required))
        {
            return await RefusedAsync($"movement needs {required}").ConfigureAwait(false);
        }

        var deadline = DateTime.UtcNow + (duration ?? DefaultMoveDuration);
        try
        {
            do
            {
                var send = await SendInputAsync(buttons, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!send.IsSuccess)
                {
                    await ReleaseAsync().ConfigureAwait(false);
                    return send;
                }

                await Task.Delay(MoveStreamInterval, cancellationToken).ConfigureAwait(false);
            } while (DateTime.UtcNow < deadline);
        }
        catch (OperationCanceledException)
        {
            await ReleaseAsync().ConfigureAwait(false);
            throw;
        }

        return await SendInputAsync(CameraButtons.None, cancellationToken: cancellationToken).ConfigureAwait(false);

        // The streamed buttons were (possibly) registered as held; never leave them pressed
        // when the stream aborts early — release best-effort, ignoring the original token.
        async Task ReleaseAsync()
        {
            try
            {
                await SendInputAsync(CameraButtons.None, cancellationToken: CancellationToken.None)
                    .ConfigureAwait(false);
            }
#pragma warning disable RCS1075 // best-effort release: a disconnected client throws; the server drops held buttons on its own once input stops
            catch (Exception)
#pragma warning restore RCS1075
            {
                // Best-effort: the server stops applying input shortly after frames cease.
            }
        }
    }

    /// <summary>Builds the client-side refusal for an action the device does not support —
    /// nothing is sent: the server acks unsupported inputs with success while ignoring them.</summary>
    /// <param name="reason">Why the action does not apply to this device.</param>
    private Task<Response> RefusedAsync(string reason) =>
        Task.FromResult(new Response
        {
            IsSuccess = false,
            Error = new ErrorMessage
            {
                Code = RustPlusErrorCode.NotSupported, Message = $"{reason}; '{CameraId}' reports {Info.ControlFlags}"
            }
        });

    /// <summary>Stops the keep-alive loop and unsubscribes from the camera (when still connected).</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _rustPlus.OnCameraRaysReceived -= ForwardFrame;
#if NET10_0_OR_GREATER
        await _cts.CancelAsync().ConfigureAwait(false);
#else
        _cts.Cancel();
#endif
        try
        {
#pragma warning disable VSTHRD003 // we own this background task; awaiting it on dispose cannot deadlock
            await _keepAlive.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the expected shutdown path.
        }

        if (_rustPlus.IsConnected)
        {
            try
            {
                await _rustPlus.UnsubscribeFromCameraAsync().ConfigureAwait(false);
            }
#pragma warning disable RCS1075 // best-effort teardown: swallow any unsubscribe failure so dispose never throws
            catch (Exception)
#pragma warning restore RCS1075
            {
                // Best-effort, mirroring rustplus.js: the server drops stale
                // subscriptions on its own; never throw from dispose.
            }
        }

        _cts.Dispose();
    }

    private void ForwardFrame(object? sender, CameraRaysEventArg frame) => OnFrameReceived?.Invoke(this, frame);

    private async Task KeepAliveAsync(TimeSpan interval)
    {
        while (!_cts.IsCancellationRequested)
        {
            await Task.Delay(interval, _cts.Token).ConfigureAwait(false);
            try
            {
                var response = await _rustPlus.SubscribeToCameraAsync(CameraId, _cts.Token).ConfigureAwait(false);
                if (response is { IsSuccess: true, Data: not null })
                {
                    Info = response.Data;
                }
                else
                {
                    OnKeepAliveFailed?.Invoke(this,
                        response.Error ?? new ErrorMessage
                        {
                            Code = RustPlusErrorCode.Unknown
                        });
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
#pragma warning disable RCS1075 // best-effort renewal: a disconnected client throws instead of returning a failed Response; keep looping so the next renewal succeeds after a reconnect
            catch (Exception ex)
#pragma warning restore RCS1075
            {
                // Renewal is best-effort: a disconnected client throws instead of returning a
                // failed Response; keep looping so the next renewal succeeds after a reconnect.
                OnKeepAliveFailed?.Invoke(this,
                    new ErrorMessage
                    {
                        Message = ex.Message, Code = RustPlusErrorCode.Unknown
                    });
            }
        }
    }
}
