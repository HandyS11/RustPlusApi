using RustPlusApi.Data;
using RustPlusApi.Data.Cameras;
using RustPlusApi.Data.Events;
using Xunit;

namespace RustPlusApi.Camera.UnitTests;

/// <summary>
/// <see cref="CameraController"/> paths that the mock-server integration suite cannot drive, because
/// they need an <see cref="RustPlusApi.Interfaces.IRustPlus"/> that <em>throws</em> instead of
/// returning a failed <see cref="Response"/>, that stays <c>IsConnected</c> while its transport is
/// broken, or that lets a renewal be held open across a dispose. Driven through
/// <see cref="FakeRustPlus"/>.
/// </summary>
public sealed class CameraControllerSeamTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Renewal cadence for tests that want the keep-alive loop to turn over promptly.</summary>
    private static readonly TimeSpan FastRenewal = TimeSpan.FromMilliseconds(20);

    /// <summary>How long a "nothing happened" assertion lets the keep-alive loop misbehave before
    /// concluding it is not running. Unlike waiting for a count to rise, this direction is
    /// contention-safe: a slow runner can only make the assertion more true, never flakily false.</summary>
    private static readonly TimeSpan SettleWindow = TimeSpan.FromMilliseconds(200);

    private static async Task<CameraController> SubscribeAsync(FakeRustPlus client, TimeSpan? interval)
    {
        var response = await CameraController.SubscribeAsync(client, "DRONE01", interval).WaitAsync(Timeout);
        Assert.True(response.IsSuccess);
        return response.Data!;
    }

    /// <summary>Polls <paramref name="condition"/> up to <see cref="Timeout"/>. Any assertion that a
    /// renewal has <em>already</em> happened has to wait for it rather than assume a wall-clock
    /// delay was enough, so a contended runner slows the test down instead of failing it.</summary>
    /// <param name="condition">The condition to poll.</param>
    /// <param name="expectation">Named in the failure message if the wait times out.</param>
    private static async Task WaitUntilAsync(Func<bool> condition, string expectation)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline && !condition())
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), $"Timed out after {Timeout.TotalSeconds:0}s waiting for {expectation}.");
    }

    [Fact]
    public async Task SubscribeAsync_WithZeroInterval_NeverRenews()
    {
        var client = new FakeRustPlus();

        var controller = await SubscribeAsync(client, TimeSpan.Zero);
        await using (controller)
        {
            // Well past several renewals had the loop been started at FastRenewal.
            await Task.Delay(SettleWindow);
            Assert.Equal(1, client.SubscribeCount);
        }

        // The initial subscribe is the only one, and dispose still unsubscribes.
        Assert.Equal(1, client.SubscribeCount);
        Assert.Equal(1, client.UnsubscribeCount);
    }

    [Fact]
    public async Task SubscribeAsync_WithNegativeInterval_NeverRenews()
    {
        var client = new FakeRustPlus();

        await using var controller = await SubscribeAsync(client, TimeSpan.FromSeconds(-1));

        await Task.Delay(SettleWindow);
        Assert.Equal(1, client.SubscribeCount);
    }

    [Fact]
    public async Task DisposeAsync_SwallowsAnUnsubscribeThatThrows()
    {
        var client = new FakeRustPlus
        {
            IsConnected = true, OnUnsubscribe = static () => throw new InvalidOperationException("socket is gone")
        };

        var controller = await SubscribeAsync(client, FastRenewal);

        // Dispose is a best-effort teardown: a throwing unsubscribe must not surface to the caller.
        await controller.DisposeAsync();

        Assert.Equal(1, client.UnsubscribeCount);
    }

    [Fact]
    public async Task DisposeAsync_SkipsTheUnsubscribe_WhenTheClientIsNotConnected()
    {
        var client = new FakeRustPlus
        {
            IsConnected = false
        };

        var controller = await SubscribeAsync(client, FastRenewal);
        await controller.DisposeAsync();

        Assert.Equal(0, client.UnsubscribeCount);
    }

    [Fact]
    public async Task KeepAlive_ReportsAnUnknownError_WhenAFailedRenewalCarriesNoErrorDetail()
    {
        var client = new FakeRustPlus
        {
            // A failed renewal whose Error is null — the server refused without detail, so the
            // controller has to synthesise one rather than hand subscribers a null.
            OnSubscribe = static (call, _) => Task.FromResult(call == 1
                ? new Response<CameraInfo?>
                {
                    IsSuccess = true, Data = FakeRustPlus.DroneInfo
                }
                : new Response<CameraInfo?>
                {
                    IsSuccess = false, Error = null
                })
        };

        await using var controller = await SubscribeAsync(client, FastRenewal);

        var failure = new TaskCompletionSource<ErrorMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        controller.OnKeepAliveFailed += (_, error) => failure.TrySetResult(error);

        var reported = await failure.Task.WaitAsync(Timeout);

        Assert.Equal(RustPlusErrorCode.Unknown, reported.Code);
        Assert.Null(reported.Message);
    }

    [Fact]
    public async Task KeepAlive_StopsQuietly_WhenARenewalIsCancelled()
    {
        var client = new FakeRustPlus
        {
            OnSubscribe = static (call, _) => call == 1
                ? Task.FromResult(new Response<CameraInfo?>
                {
                    IsSuccess = true, Data = FakeRustPlus.DroneInfo
                })
                // Cancellation landing inside the renewal call itself, rather than in the delay
                // ahead of it: the loop must return instead of reporting a keep-alive failure.
                : throw new OperationCanceledException()
        };

        var controller = await SubscribeAsync(client, FastRenewal);

        var failures = 0;
        controller.OnKeepAliveFailed += (_, _) => Interlocked.Increment(ref failures);

        // The loop returns on the OperationCanceledException, so once the second subscribe lands the
        // count is final — wait for it rather than assuming a fixed delay covered it.
        await WaitUntilAsync(() => client.SubscribeCount >= 2, "the cancelled renewal to run");
        await controller.DisposeAsync();

        Assert.Equal(0, Volatile.Read(ref failures));
        Assert.Equal(2, client.SubscribeCount);
    }

    [Fact]
    public async Task KeepAlive_ExitsThroughTheLoopCondition_WhenCancellationLandsMidRenewal()
    {
        var renewalStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRenewal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var client = new FakeRustPlus
        {
            OnSubscribe = async (call, _) =>
            {
                if (call == 1)
                {
                    return new Response<CameraInfo?>
                    {
                        IsSuccess = true, Data = FakeRustPlus.DroneInfo
                    };
                }

                // Hold the renewal open so the dispose below cancels while it is in flight, then let it
                // succeed. The loop therefore returns to its condition — rather than unwinding through a
                // cancellation — and exits there.
                renewalStarted.TrySetResult();
                await releaseRenewal.Task;
                return new Response<CameraInfo?>
                {
                    IsSuccess = true, Data = FakeRustPlus.DroneInfo
                };
            }
        };

        var controller = await SubscribeAsync(client, FastRenewal);
        await renewalStarted.Task.WaitAsync(Timeout);

        var dispose = controller.DisposeAsync();
        releaseRenewal.TrySetResult();
        await dispose.AsTask().WaitAsync(Timeout);

        Assert.Equal(2, client.SubscribeCount);
        Assert.Equal(1, client.UnsubscribeCount);
    }

    [Fact]
    public async Task KeepAlive_KeepsRunning_WhenAFailedRenewalHasNoSubscriber()
    {
        var client = new FakeRustPlus
        {
            OnSubscribe = static (call, _) => Task.FromResult(call == 1
                ? new Response<CameraInfo?>
                {
                    IsSuccess = true, Data = FakeRustPlus.DroneInfo
                }
                : new Response<CameraInfo?>
                {
                    IsSuccess = false,
                    Error = new ErrorMessage
                    {
                        Code = RustPlusErrorCode.NoPlayer
                    }
                })
        };

        await using var controller = await SubscribeAsync(client, FastRenewal);

        // Nothing is attached to OnKeepAliveFailed: reporting the failure must be a no-op rather
        // than a null dereference, and the loop must keep retrying past it.
        await WaitUntilAsync(() => client.SubscribeCount > 2, "the loop to retry past the failed renewal");
    }

    [Fact]
    public async Task IncomingFrame_IsDropped_WhenNothingIsSubscribedToOnFrameReceived()
    {
        var client = new FakeRustPlus();
        await using var controller = await SubscribeAsync(client, TimeSpan.Zero);

        // No OnFrameReceived handler attached: forwarding must be a no-op, not a null dereference.
        client.RaiseFrame(new CameraRaysEventArg());

        Assert.Equal("DRONE01", controller.CameraId);
    }

    [Fact]
    public async Task MoveAsync_WithNoButtons_Throws()
    {
        var client = new FakeRustPlus();
        await using var controller = await SubscribeAsync(client, TimeSpan.Zero);

        // The empty-bitmask arm of the guard; the integration suite covers the non-movement arm.
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => controller.MoveAsync(CameraButtons.None));

        Assert.Contains("movement buttons", ex.Message, StringComparison.Ordinal);
        Assert.Equal("buttons", ex.ParamName);
    }
}
