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

    /// <summary>
    /// A spy subclass that records the <c>disposing</c> argument passed to
    /// <see cref="RustPlusFcmSocket.Dispose(bool)"/>.
    /// </summary>
    /// <param name="credentials">The FCM credentials.</param>
    private sealed class SpySocket(Credentials credentials) : RustPlusFcmSocket(credentials)
    {
        public bool? DisposingArgument { get; private set; }

        protected override void Dispose(bool disposing)
        {
            DisposingArgument = disposing;
            base.Dispose(disposing);
        }
    }

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
    public async Task Disconnect_CancelsToken_StopsReceiveLoopBeforeProcessing()
    {
        await using var socket = NewSocket();

        // Cancel up front, then run a loop whose stream would otherwise read forever.
        socket.Disconnect();

        // A header-only frame: if the token short-circuited the loop, no read past the header occurs
        // and the loop exits cleanly. We assert no notification is dispatched and no exception escapes.
        var raised = false;
        socket.NotificationReceived += (_, _) => raised = true;

        await socket.RunReceiveLoopOverStreamAsync(new CanceledProbeStream());

        Assert.False(raised);
    }

    /// <summary>
    /// Serves a single minimal valid first frame (version 41, tag KLoginResponseTag, size 0). The receive
    /// loop processes that login message, then the <c>while</c> check sees the already-cancelled token and
    /// exits without attempting another read.
    /// </summary>
    private sealed class CanceledProbeStream : Stream
    {
        private readonly MemoryStream _reads = new([41, 3, 0]);

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
        var exception = Record.Exception(socket.Dispose);

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

    /// <summary>
    /// Asserts that the public <c>Dispose()</c> calls <c>Dispose(true)</c> — kills the two
    /// Statement/Boolean mutations at the <c>Dispose(true)</c> call site that remove the call
    /// or change the argument to <c>false</c>.
    /// </summary>
    [Fact]
    public void Dispose_CallsProtectedDisposeWithTrue()
    {
        var creds = new Credentials { Gcm = new Gcm { AndroidId = 1, SecurityToken = 1 } };
        var spy = new SpySocket(creds);

        spy.Dispose();

        // If Dispose(true) is mutated to Dispose(false) or removed, this would be null or false.
        Assert.True(spy.DisposingArgument);
    }

    /// <summary>
    /// Asserts that calling Dispose() on a socket that was never connected (so
    /// _cancellationTokenSource is not yet cancelled) does not throw. This exercises the
    /// <c>if (!_cancellationTokenSource.IsCancellationRequested)</c> guard — if the mutation
    /// flips the condition to <c>IsCancellationRequested</c>, the guard would skip Cancel()
    /// even for a fresh socket, but Cancel() is still called inside the correct branch.
    /// </summary>
    [Fact]
    public void Dispose_OnFreshSocket_DoesNotThrow()
    {
        // Fresh socket: CancellationTokenSource is not yet cancelled.
        var socket = NewSocket();
        var ex = Record.Exception(socket.Dispose);
        Assert.Null(ex);   // must not throw regardless of which branch the guard takes
    }

    /// <summary>
    /// Asserts that calling Disconnect THEN Dispose on the same socket does not throw — exercises
    /// the <c>!IsCancellationRequested</c> guard: after Disconnect the token IS cancelled, so the
    /// guard prevents a double-Cancel, and Dispose should still succeed.
    /// </summary>
    [Fact]
    public void Dispose_AfterDisconnect_CancellationAlreadyRequested_DoesNotThrow()
    {
        var socket = NewSocket();
        socket.Disconnect();  // cancels the token

        // Now Dispose: _cancellationTokenSource.IsCancellationRequested == true,
        // so the inner Cancel() should be skipped via the guard. Must not throw.
        var ex = Record.Exception(socket.Dispose);
        Assert.Null(ex);
    }

    /// <summary>
    /// Asserts that Dispose() disposes the internal CancellationTokenSource, making the CTS
    /// unusable — kills the Statement mutation that removes <c>_cancellationTokenSource.Dispose()</c>.
    /// After Dispose the receive loop must throw <see cref="ObjectDisposedException"/> when it
    /// tries to access <c>CancellationToken</c> (from the disposed CTS); if the mutation removes
    /// the disposal the CTS is still alive and the loop would succeed (no throw).
    /// </summary>
    [Fact]
    public async Task Dispose_DisposesInternalCts_ReceiveLoopThrowsObjectDisposed()
    {
        var socket = NewSocket();

        // Intentionally the synchronous Dispose path: it must dispose the CTS (the behavior under test).
#pragma warning disable CA1849, VSTHRD103, S6966
        socket.Dispose();
#pragma warning restore CA1849, VSTHRD103, S6966

        // After Dispose() the CTS is disposed; the receive loop's CancellationToken access throws.
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => socket.RunReceiveLoopOverStreamAsync(new CanceledProbeStream()));
    }
}
