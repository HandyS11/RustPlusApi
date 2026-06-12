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

    /// <summary>Occurs when a ray frame for the subscribed camera is received.</summary>
    public event EventHandler<CameraRaysEventArg>? OnFrameReceived;

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
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>The acknowledgement <see cref="Response"/> from the server.</returns>
    public Task<Response> ZoomAsync(CancellationToken cancellationToken = default) =>
        PressAsync(CameraButtons.FirePrimary, cancellationToken);

    /// <summary>
    /// Fires an auto-turret once, sending the <see cref="CameraButtons.FirePrimary"/>
    /// press-and-release used by rustplus.js.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>The acknowledgement <see cref="Response"/> from the server.</returns>
    public Task<Response> ShootAsync(CancellationToken cancellationToken = default) =>
        PressAsync(CameraButtons.FirePrimary, cancellationToken);

    /// <summary>
    /// Reloads an auto-turret, sending the <see cref="CameraButtons.Reload"/>
    /// press-and-release used by rustplus.js.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>The acknowledgement <see cref="Response"/> from the server.</returns>
    public Task<Response> ReloadAsync(CancellationToken cancellationToken = default) =>
        PressAsync(CameraButtons.Reload, cancellationToken);

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
            }
            catch (OperationCanceledException)
            {
                return;
            }
#pragma warning disable RCS1075 // best-effort renewal: a disconnected client throws instead of returning a failed Response; keep looping so the next renewal succeeds after a reconnect
            catch (Exception)
#pragma warning restore RCS1075
            {
                // Renewal is best-effort: a disconnected client throws instead of returning a
                // failed Response; keep looping so the next renewal succeeds after a reconnect.
            }
        }
    }
}
