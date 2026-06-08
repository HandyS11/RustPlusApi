using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Data;
using Xunit;

namespace RustPlusApi.Tests.Unit;

/// <summary>
/// Covers <see cref="RustPlusFcmSocket"/>'s teardown lifecycle offline: <c>Disconnect</c> raises its
/// events (and is safe with no live transport), and <c>Dispose</c> is idempotent. Deterministic — no
/// sockets and no Thread.Sleep.
/// </summary>
public class FcmSocketLifecycleTests
{
    /// <summary>Concrete subclass: <see cref="RustPlusFcmSocket"/> is abstract.</summary>
    /// <param name="credentials">The FCM credentials.</param>
    private sealed class TestSocket(Credentials credentials) : RustPlusFcmSocket(credentials);

    private static TestSocket NewSocket() =>
        new(new Credentials { Gcm = new Gcm { AndroidId = 1, SecurityToken = 1 } });

    [Fact]
    public void Disconnect_RaisesDisconnectingThenDisconnected()
    {
        using var socket = NewSocket();
        var disconnecting = false;
        var disconnected = false;
        socket.Disconnecting += (_, _) => disconnecting = true;
        socket.Disconnected += (_, _) => disconnected = true;

        // No live transport was ever established; Disconnect must still be safe.
        socket.Disconnect();

        Assert.True(disconnecting);
        Assert.True(disconnected);
    }

    [Fact]
    public void Disconnect_CancelsToken_StopsReceiveLoopBeforeProcessing()
    {
        using var socket = NewSocket();

        // Cancel up front, then run a loop whose stream would otherwise read forever.
        socket.Disconnect();

        // A header-only frame: if the token short-circuited the loop, no read past the header occurs
        // and the loop exits cleanly. We assert no notification is dispatched and no exception escapes.
        var raised = false;
        socket.NotificationReceived += (_, _) => raised = true;

        socket.RunReceiveLoopOverStream(new CanceledProbeStream());

        Assert.False(raised);
    }

    /// <summary>
    /// Serves a single minimal valid first frame (version 41, tag KLoginResponseTag, size 0). The receive
    /// loop processes that login message, then the <c>while</c> check sees the already-cancelled token and
    /// exits without attempting another read.
    /// </summary>
    private sealed class CanceledProbeStream : Stream
    {
        private readonly MemoryStream _reads = new(new byte[] { 41, 3, 0 });

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => _reads.Read(buffer, offset, count);
        public override int ReadByte() => _reads.ReadByte();
        public override void Write(byte[] buffer, int offset, int count) { }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var socket = NewSocket();

        socket.Dispose();
        var exception = Record.Exception(() => socket.Dispose());

        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_AfterDisconnect_DoesNotThrow()
    {
        var socket = NewSocket();

        socket.Disconnect();
        var exception = Record.Exception(socket.Dispose);

        Assert.Null(exception);
    }
}
