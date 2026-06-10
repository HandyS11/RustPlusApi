using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Data;
using Xunit;

namespace RustPlusApi.Tests.Unit;

/// <summary>
/// Teardown guarantee for <see cref="RustPlusFcmSocket"/>: the receive loop is tracked, and
/// <c>DisposeAsync</c> unblocks the in-progress read and awaits the loop to completion within a bounded time.
/// </summary>
public class FcmSocketTeardownTests
{
    private sealed class TestSocket(Credentials credentials) : RustPlusFcmSocket(credentials);

    private static TestSocket NewSocket() =>
        new(new Credentials { Gcm = new Gcm { AndroidId = 1, SecurityToken = 1 } });

    [Fact]
    public async Task DisposeAsync_AwaitsTrackedReceiveLoop_StopsPromptly()
    {
        var socket = NewSocket();

        // The socket takes ownership of this stream as its transport and disposes it during DisposeAsync,
        // after awaiting the loop — so disposing it here as well is safe. Suppress the ownership/async-dispose
        // analyzers that can't see that ordering guarantee.
#pragma warning disable RCS1261, CA2025
        using var stream = new GatedStream();

        // Background the receive loop; it consumes the login frame then parks in a blocking read.
        var loop = socket.RunReceiveLoopOverStreamAsync(stream);
#pragma warning restore RCS1261, CA2025

        await Task.Delay(100);
        Assert.False(loop.IsCompleted); // proves the loop is genuinely blocked, not already done

        await socket.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(loop.IsCompleted); // DisposeAsync awaited the tracked loop to completion
    }

    /// <summary>
    /// Serves a single valid login frame (version 41, tag KLoginResponseTag, size 0) then blocks every
    /// subsequent read until the stream is disposed, at which point reads report EOF so the loop exits.
    /// </summary>
    private sealed class GatedStream : Stream
    {
        private readonly Queue<int> _initial = new([41, 3, 0]);
        private readonly ManualResetEventSlim _gate = new(false);

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int ReadByte()
        {
            if (_initial.Count > 0)
            {
                return _initial.Dequeue();
            }

            _gate.Wait();
            return -1; // EOF once released by Dispose
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count == 0)
            {
                return 0;
            }

            if (_initial.Count > 0)
            {
                var n = 0;
                while (n < count && _initial.Count > 0)
                {
                    buffer[offset + n++] = (byte)_initial.Dequeue();
                }
                return n;
            }

            _gate.Wait();
            return 0; // EOF once released by Dispose
        }

        public override void Write(byte[] buffer, int offset, int count) { }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            _gate.Set(); // release any blocked read so the loop can observe EOF
            if (disposing)
            {
                _gate.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
