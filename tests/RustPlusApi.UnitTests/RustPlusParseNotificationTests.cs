using RustPlusContracts;
using Xunit;

namespace RustPlusApi.UnitTests;

/// <summary>
/// Covers <see cref="RustPlus.ParseNotification"/>:
/// - the null-broadcast guard
/// - every event's no-subscriber (event == null) false-branch
/// - <see cref="RustPlusSocket.IsError(RustPlusContracts.AppMessage)"/> Broadcast-set path
/// - <see cref="RustPlusSocket.GetErrorMessage(RustPlusContracts.AppMessage)"/> no-error fallback path
/// - <see cref="RustPlusSocket.Dispose()"/> already-cancelled token path
/// </summary>
public class RustPlusParseNotificationTests
{
    /// <summary>Exposes the protected members of <see cref="RustPlus"/> for direct unit testing.</summary>
    private sealed class TestRustPlus() : RustPlus("127.0.0.1", 1, 1, 1)
    {
        public void Feed(AppBroadcast? b) => ParseNotification(b);

        /// <summary>Delegates to the inherited protected static IsError.</summary>
        /// <param name="m">The message to check.</param>
        public static bool CallIsError(AppMessage m) => IsError(m);

        /// <summary>Delegates to the inherited protected static GetErrorMessage.</summary>
        /// <param name="m">The message to extract the error from.</param>
        public static string CallGetErrorMessage(AppMessage m) => GetErrorMessage(m);
    }

    // ── null guard ────────────────────────────────────────────────────────────

    [Fact]
    public void ParseNotification_NullBroadcast_DoesNothing()
    {
        using var sut = new TestRustPlus();

        var ex = Record.Exception(() => sut.Feed(null));

        Assert.Null(ex);
    }

    // ── no-subscriber false-branches (?.Invoke with no handler attached) ─────

    [Fact]
    public void SmartSwitch_WithSubscriber_InvokesHandler()
    {
        using var sut = new TestRustPlus();
        RustPlusApi.Data.Events.SmartSwitchEventArg? captured = null;
        sut.OnSmartSwitchTriggered += (_, e) => captured = e;

        var broadcast = new AppBroadcast
        {
            EntityChanged = new AppEntityChanged
            {
                EntityId = 42,
                Payload = new AppEntityPayload { Value = true, Capacity = 0 }
            }
        };

        sut.Feed(broadcast);

        Assert.NotNull(captured);
        Assert.Equal(42u, captured!.Id);
        Assert.True(captured.IsActive);
    }

    [Fact]
    public void SmartSwitch_NoSubscriber_DoesNotThrow()
    {
        using var sut = new TestRustPlus();
        // OnSmartSwitchTriggered intentionally NOT subscribed

        var broadcast = new AppBroadcast
        {
            EntityChanged = new AppEntityChanged
            {
                EntityId = 1,
                Payload = new AppEntityPayload { Value = true, Capacity = 0 }
            }
        };

        var ex = Record.Exception(() => sut.Feed(broadcast));
        Assert.Null(ex);
    }

    [Fact]
    public void StorageMonitor_NoSubscriber_DoesNotThrow()
    {
        using var sut = new TestRustPlus();
        // OnStorageMonitorTriggered intentionally NOT subscribed

        var broadcast = new AppBroadcast
        {
            EntityChanged = new AppEntityChanged
            {
                EntityId = 2,
                Payload = new AppEntityPayload { Value = false, Capacity = 48 } // Capacity != 0 → storage monitor
            }
        };

        var ex = Record.Exception(() => sut.Feed(broadcast));
        Assert.Null(ex);
    }

    [Fact]
    public void TeamMessage_NoSubscriber_DoesNotThrow()
    {
        using var sut = new TestRustPlus();
        // OnTeamChatReceived intentionally NOT subscribed

        var broadcast = new AppBroadcast
        {
            TeamMessage = new AppNewTeamMessage
            {
                Message = new AppTeamMessage
                {
                    SteamId = 76561198000000001,
                    Name = "Alice",
                    Message = "hello",
                    Color = "#FFFFFF",
                    Time = 1_700_000_000
                }
            }
        };

        var ex = Record.Exception(() => sut.Feed(broadcast));
        Assert.Null(ex);
    }

    [Fact]
    public void ClanMessage_NoSubscriber_DoesNotThrow()
    {
        using var sut = new TestRustPlus();
        // OnClanChatReceived intentionally NOT subscribed

        var broadcast = new AppBroadcast
        {
            ClanMessage = new AppNewClanMessage
            {
                ClanId = 42,
                Message = new AppClanMessage
                {
                    SteamId = 76561198000000001,
                    Name = "Bob",
                    Message = "clan chat",
                    Time = 1_700_000_000
                }
            }
        };

        var ex = Record.Exception(() => sut.Feed(broadcast));
        Assert.Null(ex);
    }

    [Fact]
    public void ClanChanged_NoSubscriber_DoesNotThrow()
    {
        using var sut = new TestRustPlus();
        // OnClanChanged intentionally NOT subscribed

        var broadcast = new AppBroadcast
        {
            ClanChanged = new AppClanChanged
            {
                ClanInfo = new ClanInfo { ClanId = 1, Name = "TestClan" }
            }
        };

        var ex = Record.Exception(() => sut.Feed(broadcast));
        Assert.Null(ex);
    }

    [Fact]
    public void CameraRays_NoSubscriber_DoesNotThrow()
    {
        using var sut = new TestRustPlus();
        // OnCameraRaysReceived intentionally NOT subscribed

        var broadcast = new AppBroadcast
        {
            CameraRays = new AppCameraRays
            {
                VerticalFov = 65f,
                SampleOffset = 0,
                RayData = [0, 1, 2],
                Distance = 50f
            }
        };

        var ex = Record.Exception(() => sut.Feed(broadcast));
        Assert.Null(ex);
    }

    // ── IsError: message with Broadcast set → returns false ──────────────────

    [Fact]
    public void IsError_WhenBroadcastIsSet_ReturnsFalse()
    {
        var message = new AppMessage
        {
            Broadcast = new AppBroadcast
            {
                TeamMessage = new AppNewTeamMessage()
            }
        };

        var result = TestRustPlus.CallIsError(message);

        Assert.False(result);
    }

    // ── GetErrorMessage: non-error response → "unknown-error" fallback ───────

    [Fact]
    public void GetErrorMessage_WhenNoError_ReturnsUnknownError()
    {
        var message = new AppMessage
        {
            Response = new AppResponse
            {
                Seq = 1,
                Success = new AppSuccess()
                // Error is null
            }
        };

        var result = TestRustPlus.CallGetErrorMessage(message);

        Assert.Equal("unknown-error", result);
    }

    // ── Dispose: calling Dispose after cancel → no throw ─────────────────────

    [Fact]
    public void Dispose_WhenAlreadyCancelled_DoesNotThrow()
    {
        var sut = new TestRustPlus();
        // First dispose cancels the token
        sut.Dispose();
        // Second dispose: token is already cancelled — hits the false branch of
        // `if (!_cancellationTokenSource.IsCancellationRequested)`
        var ex = Record.Exception(sut.Dispose);
        Assert.Null(ex);
    }
}
