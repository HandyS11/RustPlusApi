using McsProto;
using ProtoBuf;
using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Data;
using Xunit;
using static RustPlusApi.Fcm.Data.Tags;
using static RustPlusApi.Fcm.Utils.McsUtils;

namespace RustPlusApi.Tests.Unit;

/// <summary>
/// Drives <see cref="RustPlusFcmSocket"/>'s MCS framing/dispatch loop fully offline through the
/// <c>RunReceiveLoopOverStream</c> seam. A scripted duplex stream feeds server→client frames and
/// captures client→server writes, so every assertion is deterministic — no sockets, no Thread.Sleep.
/// </summary>
public class FcmSocketFramingTests
{
    private const int KMcsVersion = 41;

    /// <summary>Concrete subclass: <see cref="RustPlusFcmSocket"/> is abstract.</summary>
    /// <param name="credentials">The FCM credentials.</param>
    /// <param name="persistentIds">The de-duplication set of already-seen persistent ids.</param>
    private sealed class TestSocket(Credentials credentials, ICollection<string>? persistentIds = null)
        : RustPlusFcmSocket(credentials, persistentIds);

    private static Credentials NewCredentials() =>
        new() { Gcm = new Gcm { AndroidId = 1, SecurityToken = 1 } };

    private static TestSocket NewSocket(ICollection<string>? persistentIds = null) =>
        new(NewCredentials(), persistentIds);

    /// <summary>
    /// A duplex stream whose reads are served from a pre-built script and whose writes are captured.
    /// The script always ends with a Close frame so the receive loop terminates deterministically;
    /// the Close handler cancels the token, exiting the <c>while</c> on the next iteration.
    /// </summary>
    /// <param name="script">The pre-built MCS byte script served to reads.</param>
    private sealed class ScriptedStream(byte[] script) : Stream
    {
        private readonly MemoryStream _reads = new(script);
        public MemoryStream Writes { get; } = new();

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => _reads.Read(buffer, offset, count);
        public override int ReadByte() => _reads.ReadByte();
        public override void Write(byte[] buffer, int offset, int count) => Writes.Write(buffer, offset, count);
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    }

    /// <summary>Serializes <paramref name="message"/> to its MCS payload bytes.</summary>
    /// <param name="message">The protobuf message to serialize.</param>
    private static byte[] PayloadOf(object message)
    {
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, message);
        return ms.ToArray();
    }

    /// <summary>Builds the very first MCS frame: [version][tag] varint(size) payload.</summary>
    /// <param name="tag">The MCS tag identifying the message type.</param>
    /// <param name="message">The protobuf message carried by the frame.</param>
    private static IEnumerable<byte> FirstFrame(McsProtoTag tag, object message)
    {
        var payload = PayloadOf(message);
        return new byte[] { KMcsVersion, (byte)(int)tag }
            .Concat(EncodeVarInt32(payload.Length))
            .Concat(payload);
    }

    /// <summary>Builds a first frame with an explicit version byte (for the unsupported-version case).</summary>
    /// <param name="version">The MCS protocol version byte to emit.</param>
    /// <param name="tag">The MCS tag identifying the message type.</param>
    /// <param name="message">The protobuf message carried by the frame.</param>
    private static IEnumerable<byte> FirstFrame(int version, McsProtoTag tag, object message)
    {
        var payload = PayloadOf(message);
        return new byte[] { (byte)version, (byte)(int)tag }
            .Concat(EncodeVarInt32(payload.Length))
            .Concat(payload);
    }

    /// <summary>Builds a subsequent MCS frame: [tag] varint(size) payload (no version byte).</summary>
    /// <param name="tag">The MCS tag identifying the message type.</param>
    /// <param name="message">The protobuf message carried by the frame.</param>
    private static IEnumerable<byte> NextFrame(McsProtoTag tag, object message)
    {
        var payload = PayloadOf(message);
        return new byte[] { (byte)(int)tag }
            .Concat(EncodeVarInt32(payload.Length))
            .Concat(payload);
    }

    /// <summary>Concatenates the given frames into a single MCS byte script.</summary>
    /// <param name="frames">The frames to concatenate in order.</param>
    private static byte[] Build(params IEnumerable<byte>[] frames) =>
        frames.SelectMany(f => f).ToArray();

    /// <summary>Builds a valid Rust+ data-message stanza (channelId + body present).</summary>
    /// <param name="persistentId">The stanza's persistent id used for de-duplication.</param>
    /// <param name="body">The JSON body payload.</param>
    private static DataMessageStanza RustNotification(string persistentId = "p1", string body = "{}") =>
        new()
        {
            From = "123456789",
            PersistentId = persistentId,
            Sent = 1_700_000_000_000,
            AppDatas =
            {
                new AppData { Key = "channelId", Value = "pairing" },
                new AppData { Key = "body", Value = body },
            }
        };

    [Fact]
    public void LoginResponseThenDataMessage_RaisesNotificationReceived()
    {
        using var socket = NewSocket();
        string? notification = null;
        socket.NotificationReceived += (_, n) => notification = n;

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification()),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        socket.RunReceiveLoopOverStream(new ScriptedStream(script));

        Assert.NotNull(notification);
        Assert.Contains("123456789", notification, StringComparison.Ordinal);
    }

    [Fact]
    public void HeartbeatPing_WritesHeartbeatAckBackToStream()
    {
        using var socket = NewSocket();

        var stream = new ScriptedStream(Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KHeartbeatPingTag, new HeartbeatPing { StreamId = 7, Status = 0 }),
            NextFrame(McsProtoTag.KCloseTag, new Close())));

        socket.RunReceiveLoopOverStream(stream);

        // Decode the bytes the socket wrote back: [version][tag] varint(size) payload.
        var written = stream.Writes.ToArray();
        Assert.True(written.Length >= 2);
        Assert.Equal(KMcsVersion, written[0]);
        Assert.Equal((byte)(int)McsProtoTag.KHeartbeatAckTag, written[1]);

        // Skip the varint size, then deserialize the HeartbeatAck payload.
        var idx = 2;
        while ((written[idx] & 0x80) != 0) idx++;
        idx++;
        using var payload = new MemoryStream(written, idx, written.Length - idx);
        var ack = Serializer.Deserialize<HeartbeatAck>(payload);
        Assert.Equal(8, ack.StreamId);           // ping.StreamId (7) + 1
        Assert.Equal(7, ack.LastStreamIdReceived);
    }

    [Fact]
    public void CloseTag_RaisesSocketClosedAndDisconnects()
    {
        using var socket = NewSocket();
        var socketClosed = false;
        var disconnected = false;
        socket.SocketClosed += (_, _) => socketClosed = true;
        socket.Disconnected += (_, _) => disconnected = true;

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        socket.RunReceiveLoopOverStream(new ScriptedStream(script));

        Assert.True(socketClosed);
        Assert.True(disconnected);
    }

    [Fact]
    public void UnsupportedVersion_ThrowsInvalidOperationException()
    {
        using var socket = NewSocket();

        var script = Build(FirstFrame(37, McsProtoTag.KLoginResponseTag, new LoginResponse()));

        var ex = Assert.Throws<InvalidOperationException>(
            () => socket.RunReceiveLoopOverStream(new ScriptedStream(script)));
        Assert.Contains("unsupported", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FirstMessageNotLoginResponse_ThrowsInvalidOperationException()
    {
        using var socket = NewSocket();

        var script = Build(FirstFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification()));

        var ex = Assert.Throws<InvalidOperationException>(
            () => socket.RunReceiveLoopOverStream(new ScriptedStream(script)));
        Assert.Contains(nameof(LoginResponse), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DataMessageMissingChannelId_IsIgnored()
    {
        using var socket = NewSocket();
        var raised = false;
        socket.NotificationReceived += (_, _) => raised = true;

        var stanza = new DataMessageStanza
        {
            From = "1",
            PersistentId = "p-missing",
            AppDatas = { new AppData { Key = "body", Value = "{}" } }   // no channelId
        };

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, stanza),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        socket.RunReceiveLoopOverStream(new ScriptedStream(script));

        Assert.False(raised);
    }

    [Fact]
    public void DataMessageMissingBody_IsIgnored()
    {
        using var socket = NewSocket();
        var raised = false;
        socket.NotificationReceived += (_, _) => raised = true;

        var stanza = new DataMessageStanza
        {
            From = "1",
            PersistentId = "p-missing-body",
            AppDatas = { new AppData { Key = "channelId", Value = "pairing" } }   // no body
        };

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, stanza),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        socket.RunReceiveLoopOverStream(new ScriptedStream(script));

        Assert.False(raised);
    }

    [Fact]
    public void DuplicatePersistentId_IsSkipped()
    {
        // The LoginResponse handler clears the dedupe set, so seeding it up front would not survive.
        // Instead send the same PersistentId twice: the first populates the set, the second is skipped.
        using var socket = NewSocket([]);
        var count = 0;
        socket.NotificationReceived += (_, _) => count++;

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification(persistentId: "dup-1")),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification(persistentId: "dup-1")),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        socket.RunReceiveLoopOverStream(new ScriptedStream(script));

        Assert.Equal(1, count);   // first delivered, duplicate skipped
    }

    [Fact]
    public void ReadVarInt32_MultiByteSize_FrameDelivered()
    {
        // Build a DataMessageStanza whose serialized payload length >= 128, so the size varint
        // requires a continuation byte (multi-byte encoding path in ReadVarInt32).
        using var socket = NewSocket();
        string? notification = null;
        socket.NotificationReceived += (_, n) => notification = n;

        // Pad the body to make payload > 128 bytes.
        var longBody = "{\"img\":\"\",\"url\":\"\",\"desc\":\"" + new string('x', 120) + "\"}";
        var bigStanza = new DataMessageStanza
        {
            From = "123456789",
            PersistentId = "big-1",
            Sent = 1_700_000_000_000,
            AppDatas =
            {
                new AppData { Key = "channelId", Value = "pairing" },
                new AppData { Key = "body", Value = longBody },
            }
        };

        // Confirm the payload actually crosses the 128-byte boundary.
        var payloadBytes = PayloadOf(bigStanza);
        Assert.True(payloadBytes.Length >= 128, $"Expected payload >= 128 bytes, got {payloadBytes.Length}");

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, bigStanza),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        socket.RunReceiveLoopOverStream(new ScriptedStream(script));

        Assert.NotNull(notification);
    }

    [Fact]
    public void OnGotMessageBytes_CorruptPayload_RaisesErrorOccurred()
    {
        // After a valid LoginResponse, send a frame whose tag maps to DataMessageStanza but whose
        // payload is random bytes that protobuf-net cannot deserialize.  OnGotMessageBytes must
        // catch the exception and fire ErrorOccurred instead of crashing the loop.
        using var socket = NewSocket();
        Exception? error = null;
        socket.ErrorOccurred += (_, ex) => error = ex;

        // Manually build a frame: [tag][varint(size)][corrupt-payload]
        var corruptPayload = new byte[] { 0xFF, 0xFE, 0xFD, 0xFC, 0xFB, 0xFA, 0xF9 };
        var corruptFrame =
            new byte[] { (byte)(int)McsProtoTag.KDataMessageStanzaTag }
            .Concat(EncodeVarInt32(corruptPayload.Length))
            .Concat(corruptPayload);

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            corruptFrame,
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        socket.RunReceiveLoopOverStream(new ScriptedStream(script));

        Assert.NotNull(error);
    }

    [Fact]
    public void IqStanza_IsIgnored_NoNotificationAndNoThrow()
    {
        // The KIqStanzaTag case in OnMessage just breaks — assert no crash and no notification.
        using var socket = NewSocket();
        var raised = false;
        socket.NotificationReceived += (_, _) => raised = true;

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KIqStanzaTag, new IqStanza()),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        socket.RunReceiveLoopOverStream(new ScriptedStream(script));

        Assert.False(raised);
    }

    [Fact]
    public void UnrecognizedTag_HeartbeatAck_IsIgnored_NoNotificationAndNoThrow()
    {
        // HeartbeatAck is a known protobuf type but has no explicit handling in OnMessage,
        // so it falls through to the default arm (Debug.WriteLine + ignore).
        using var socket = NewSocket();
        var raised = false;
        socket.NotificationReceived += (_, _) => raised = true;

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KHeartbeatAckTag, new HeartbeatAck()),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        socket.RunReceiveLoopOverStream(new ScriptedStream(script));

        Assert.False(raised);
    }

    // ── OnDataMessage optional-field branches ────────────────────────────────

    /// <summary>
    /// Sends a DataMessageStanza with ALL optional AppData fields present (title, projectId,
    /// experienceId, scopeKey, message). Covers the "not-null" arm of every <c>??</c> null-
    /// coalescing operator in <c>OnDataMessage</c>.
    /// </summary>
    [Fact]
    public void DataMessage_AllOptionalFields_Present_Delivered()
    {
        using var socket = NewSocket([]);
        string? notification = null;
        socket.NotificationReceived += (_, n) => notification = n;

        var stanza = new DataMessageStanza
        {
            From = "123456789",
            PersistentId = "opt-full",
            Sent = 1_700_000_000_000,
            AppDatas =
            {
                new AppData { Key = "channelId", Value = "pairing" },
                new AppData { Key = "body", Value = "{}" },
                new AppData { Key = "title", Value = "Test Title" },
                new AppData { Key = "projectId", Value = "00000000-0000-0000-0000-000000000001" },
                new AppData { Key = "experienceId", Value = "@scope/exp" },
                new AppData { Key = "scopeKey", Value = "myScope" },
                new AppData { Key = "message", Value = "hello world" },
            }
        };

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, stanza),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        socket.RunReceiveLoopOverStream(new ScriptedStream(script));

        Assert.NotNull(notification);
        // Verify the extra fields were actually captured in the serialized JSON.
        Assert.Contains("Test Title", notification, StringComparison.Ordinal);
        Assert.Contains("hello world", notification, StringComparison.Ordinal);
    }

    /// <summary>
    /// Sends a DataMessageStanza with <c>PersistentId = null</c>. Covers the
    /// <c>dataMessage.PersistentId is not null</c> false-branch (line 431) and the
    /// <c>dataMessage?.PersistentId != null</c> short-circuit false-branch (line 385).
    /// </summary>
    [Fact]
    public void DataMessage_NullPersistentId_DeliveredAndNotAddedToDedupeSet()
    {
        var ids = new List<string>();
        using var socket = NewSocket(ids);
        var count = 0;
        socket.NotificationReceived += (_, _) => count++;

        // Build a stanza with no PersistentId field set (null).
        var stanza = new DataMessageStanza
        {
            From = "123456789",
            // PersistentId deliberately left null
            Sent = 1_700_000_000_000,
            AppDatas =
            {
                new AppData { Key = "channelId", Value = "pairing" },
                new AppData { Key = "body", Value = "{}" },
            }
        };

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, stanza),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        socket.RunReceiveLoopOverStream(new ScriptedStream(script));

        Assert.Equal(1, count);           // message was delivered
        Assert.Empty(ids);                // nothing added to the dedupe set
    }

    /// <summary>
    /// Sends a DataMessageStanza with <c>Sent = null</c>.  Covers the
    /// <c>dataMessage.Sent ?? 0</c> null branch (SentAt falls back to epoch).
    /// </summary>
    [Fact]
    public void DataMessage_NullSent_FallsBackToEpoch()
    {
        using var socket = NewSocket([]);
        string? notification = null;
        socket.NotificationReceived += (_, n) => notification = n;

        var stanza = new DataMessageStanza
        {
            From = "123456789",
            PersistentId = "sent-null",
            // Sent deliberately not set (null)
            AppDatas =
            {
                new AppData { Key = "channelId", Value = "pairing" },
                new AppData { Key = "body", Value = "{}" },
            }
        };

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, stanza),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        socket.RunReceiveLoopOverStream(new ScriptedStream(script));

        Assert.NotNull(notification);
        // When Sent is null the SentAt maps to DateTime.UnixEpoch; the serialized JSON
        // should not contain a large millisecond timestamp.
        Assert.Contains("1970", notification, StringComparison.Ordinal);
    }

    /// <summary>
    /// Sends a valid DataMessageStanza with no <see cref="NotificationReceived"/> subscriber.
    /// Covers the <c>NotificationReceived?.Invoke</c> null-conditional no-subscriber false-branch.
    /// </summary>
    [Fact]
    public void DataMessage_NoNotificationReceivedSubscriber_DoesNotThrow()
    {
        using var socket = NewSocket([]);
        // NotificationReceived intentionally NOT subscribed

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification("no-sub")),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        var ex = Record.Exception(() => socket.RunReceiveLoopOverStream(new ScriptedStream(script)));
        Assert.Null(ex);
    }

    /// <summary>
    /// Drives <c>OnGotMessageBytes</c> with an empty payload (data.Length == 0).
    /// This covers the early-return path that calls <c>Activator.CreateInstance</c>
    /// instead of deserializing.
    /// </summary>
    [Fact]
    public void EmptyPayload_OnGotMessageBytes_DispatchesDefaultInstance()
    {
        // Manually build a HeartbeatAck frame with a zero-length payload (size varint = 0)
        // after the LoginResponse, then close.
        using var socket = NewSocket();
        var raised = false;
        socket.NotificationReceived += (_, _) => raised = true;

        var emptyFrame =
            new byte[] { (byte)(int)McsProtoTag.KHeartbeatAckTag }
            .Concat(EncodeVarInt32(0));   // zero-length payload

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            emptyFrame,
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        var ex = Record.Exception(() => socket.RunReceiveLoopOverStream(new ScriptedStream(script)));

        Assert.Null(ex);
        Assert.False(raised);   // HeartbeatAck hits the default arm → ignored, no notification
    }

    /// <summary>
    /// Drives <c>OnDataMessage</c> with a null DataMessageStanza reference (i.e. the
    /// <c>e.Object as DataMessageStanza</c> cast returns null). This covers the
    /// <c>dataMessage?.PersistentId != null</c> outer null-check false-branch and the
    /// <c>dataMessage?.AppDatas is not { Count: &gt; 0 }</c> null-propagation path.
    /// </summary>
    [Fact]
    public void DataMessage_NullStanza_IsIgnoredWithoutThrow()
    {
        // Send a tag that maps to DataMessageStanza but deserializes as an empty/null stanza
        // by sending a zero-length payload — Serializer produces a default instance with
        // null AppDatas list, which matches the "no AppDatas" guard.
        using var socket = NewSocket();
        var raised = false;
        socket.NotificationReceived += (_, _) => raised = true;

        var zeroLengthDataFrame =
            new byte[] { (byte)(int)McsProtoTag.KDataMessageStanzaTag }
            .Concat(EncodeVarInt32(0));

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            zeroLengthDataFrame,
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        socket.RunReceiveLoopOverStream(new ScriptedStream(script));

        Assert.False(raised);
    }

    /// <summary>
    /// Covers the <c>persistentIds == null</c> arm of <c>OnDataMessage</c>'s dedupe guard.
    /// When no <c>persistentIds</c> collection is provided the null-conditional
    /// <c>persistentIds?.Contains(...)</c> returns null, short-circuiting the condition.
    /// </summary>
    [Fact]
    public void DataMessage_NullPersistentIdsCollection_DeliversSameMessageTwice()
    {
        // persistentIds = null means no de-duplication at all.
        using var socket = NewSocket(persistentIds: null);
        var count = 0;
        socket.NotificationReceived += (_, _) => count++;

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification("same-id")),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification("same-id")),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        socket.RunReceiveLoopOverStream(new ScriptedStream(script));

        Assert.Equal(2, count);   // both delivered; no deduplication without the set
    }
}
