using McsProto;
using ProtoBuf;
using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Data;
using Xunit;
using static RustPlusApi.Fcm.Data.Tags;
using static RustPlusApi.Fcm.Utils.McsUtils;

namespace RustPlusApi.Fcm.UnitTests;

/// <summary>
/// Drives <see cref="RustPlusFcmSocket"/>'s MCS framing/dispatch loop fully offline through the
/// <c>RunReceiveLoopOverStream</c> seam. A scripted duplex stream feeds server→client frames and
/// captures client→server writes, so every assertion is deterministic — no sockets, no Thread.Sleep.
/// </summary>
public class FcmSocketFramingTests
{
    private const int KMcsVersion = 41;

    private static Credentials NewCredentials() =>
        new()
        {
            Gcm = new Gcm
            {
                AndroidId = 1, SecurityToken = 1
            }
        };

    private static TestSocket NewSocket(ICollection<string>? persistentIds = null) =>
        new(NewCredentials(), persistentIds);

    /// <summary>
    /// Splits the client-written byte stream into MCS frames. Client frames after the login
    /// request are bare <c>[tag][varint-size][payload]</c> — no version byte.
    /// </summary>
    /// <param name="written">The raw bytes the socket wrote.</param>
    private static List<(int Tag, byte[] Payload)> ParseClientFrames(byte[] written)
    {
        var frames = new List<(int, byte[])>();
        var i = 0;
        while (i < written.Length)
        {
            int tag = written[i++];
            var size = 0;
            var shift = 0;
            while (true)
            {
                var b = written[i++];
                size |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    break;
                }

                shift += 7;
            }

            frames.Add((tag, written.Skip(i).Take(size).ToArray()));
            i += size;
        }

        return frames;
    }

    [Fact]
    public async Task ReceiveLoop_OverAsyncOnlyStream_ProcessesFramesViaAsyncIo()
    {
        await using var socket = NewSocket();
        string? notification = null;
        socket.NotificationReceived += (_, n) => notification = n;

        var stream = new AsyncOnlyStream(Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification()),
            NextFrame(McsProtoTag.KHeartbeatPingTag, new HeartbeatPing
            {
                StreamId = 1, Status = 0
            }),
            NextFrame(McsProtoTag.KCloseTag, new Close())));

        // Must complete using async I/O only: synchronous Read/Write on AsyncOnlyStream throw.
        await socket.RunReceiveLoopOverStreamAsync(stream);

        Assert.NotNull(notification);
        // The ping-ack must have been written back via WriteAsync.
        Assert.True(stream.Writes.ToArray().Length >= 2);
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
        return new byte[]
            {
                KMcsVersion, (byte)(int)tag
            }
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
        return new byte[]
            {
                (byte)version, (byte)(int)tag
            }
            .Concat(EncodeVarInt32(payload.Length))
            .Concat(payload);
    }

    /// <summary>Builds a subsequent MCS frame: [tag] varint(size) payload (no version byte).</summary>
    /// <param name="tag">The MCS tag identifying the message type.</param>
    /// <param name="message">The protobuf message carried by the frame.</param>
    private static IEnumerable<byte> NextFrame(McsProtoTag tag, object message)
    {
        var payload = PayloadOf(message);
        return new byte[]
            {
                (byte)(int)tag
            }
            .Concat(EncodeVarInt32(payload.Length))
            .Concat(payload);
    }

    /// <summary>Concatenates the given frames into a single MCS byte script.</summary>
    /// <param name="frames">The frames to concatenate in order.</param>
    private static byte[] Build(params IEnumerable<byte>[] frames) =>
        [.. frames.SelectMany(f => f)];

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
                new AppData
                {
                    Key = "channelId", Value = "pairing"
                },
                new AppData
                {
                    Key = "body", Value = body
                },
            }
        };

    [Fact]
    public async Task LoginResponseThenDataMessage_RaisesNotificationReceived()
    {
        await using var socket = NewSocket();
        string? notification = null;
        socket.NotificationReceived += (_, n) => notification = n;

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification()),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        await socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script));

        Assert.NotNull(notification);
        // The delivered notification is the serialized FcmMessage; assert exact parsed fields.
        using var doc = System.Text.Json.JsonDocument.Parse(notification!);
        var root = doc.RootElement;
        Assert.Equal(123456789L, root.GetProperty("From").GetInt64());
        Assert.Equal("p1", root.GetProperty("PersistentId").GetString());
        Assert.Equal("pairing", root.GetProperty("Data").GetProperty("ChannelId").GetString());
    }

    [Fact]
    public async Task DataMessage_BodyDeserializesToNull_IsSkippedWithoutFaulting()
    {
        // A JSON-literal null body deserializes to null without throwing; the message must be
        // skipped with a log instead of deferring a NullReferenceException into downstream event handlers.
        await using var socket = NewSocket();
        string? notification = null;
        Exception? error = null;
        socket.NotificationReceived += (_, n) => notification = n;
        socket.ErrorOccurred += (_, ex) => error = ex;

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification(body: "null")));

        await socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script));

        Assert.Null(notification); // skipped, not dispatched with a null Body
        Assert.Null(error); // and skipped cleanly, not via the catch-all
    }

    [Fact]
    public async Task HeartbeatPing_WritesHeartbeatAckBackToStream()
    {
        await using var socket = NewSocket();

        var stream = new ScriptedStream(Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KHeartbeatPingTag, new HeartbeatPing
            {
                StreamId = 7, Status = 0
            }),
            NextFrame(McsProtoTag.KCloseTag, new Close())));

        await socket.RunReceiveLoopOverStreamAsync(stream);

        // Post-login client frames are bare [tag][varint-size][payload]: the MCS version byte is
        // only ever sent with the initial LoginRequest. A stray version byte here desyncs the
        // server's parser and gets the connection closed.
        var (tag, payload) = Assert.Single(ParseClientFrames(stream.Writes.ToArray()));
        Assert.Equal((int)McsProtoTag.KHeartbeatAckTag, tag);

        var ack = Serializer.Deserialize<HeartbeatAck>(new MemoryStream(payload));
        // The ack reports OUR incoming stream position (LoginResponse = 1, ping = 2), not an echo
        // of the ping's own ids — that's what tells the server its frames were received.
        Assert.Equal(2, ack.LastStreamIdReceived);
        Assert.Null(ack.StreamId); // outgoing stream ids are implicit (frame count), never set
    }

    /// <summary>
    /// Receiving a <see cref="DataMessageStanza"/> must be acknowledged with a StreamAck IqStanza
    /// carrying <c>LastStreamIdReceived</c>. Without it the server treats the message as undelivered:
    /// it closes the connection after ~5 minutes to force redelivery, and replays the message on
    /// the next connect — both observed live.
    /// </summary>
    [Fact]
    public async Task DataMessage_WritesStreamAckAcknowledgingReceivedFrames()
    {
        await using var socket = NewSocket();

        var stream = new ScriptedStream(Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification()),
            NextFrame(McsProtoTag.KCloseTag, new Close())));

        await socket.RunReceiveLoopOverStreamAsync(stream);

        var (tag, payload) = Assert.Single(ParseClientFrames(stream.Writes.ToArray()));
        Assert.Equal((int)McsProtoTag.KIqStanzaTag, tag);

        var iq = Serializer.Deserialize<IqStanza>(new MemoryStream(payload));
        Assert.Equal(IqStanza.IqType.Set, iq.Type);
        Assert.Equal(string.Empty, iq.Id);
        Assert.NotNull(iq.Extension);
        Assert.Equal(13, iq.Extension!.Id); // kStreamAck extension id (Chromium mcs_client)
        Assert.Equal(2, iq.LastStreamIdReceived); // LoginResponse = 1, DataMessageStanza = 2
    }

    /// <summary>
    /// The periodic client heartbeat must piggyback <c>LastStreamIdReceived</c> so the server sees
    /// its frames acknowledged even when no StreamAck happened to be sent since.
    /// </summary>
    [Fact]
    public async Task HeartbeatPing_AfterReceivedFrames_CarriesLastStreamIdReceived()
    {
        var options = new RustPlusFcmSocketOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(30), InactivityTimeout = TimeSpan.FromSeconds(30)
        };
        await using var socket = new TestSocket(NewCredentials(), null, options);

        // Phase 1: receive login + data over the seam. No Close frame, so the instance token stays
        // live (the stream just hits EOF) and the heartbeat loop can run afterwards.
        await socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification()))));

        // Phase 2: run the heartbeat loop over a fresh capture stream and grab the first ping.
        var transport = new ScriptedStream([]);
#pragma warning disable CA2025 // the loop is awaited to completion below, before the stream leaves scope
        var loop = socket.RunHeartbeatLoopOverStreamAsync(transport);
#pragma warning restore CA2025
        await transport.FirstWrite.WaitAsync(TimeSpan.FromSeconds(10));
        socket.Disconnect(); // cancels the token; the loop exits on its next delay
        await loop.WaitAsync(TimeSpan.FromSeconds(5));

        var frames = ParseClientFrames(transport.Writes.ToArray());
        Assert.NotEmpty(frames);
        Assert.Equal((int)McsProtoTag.KHeartbeatPingTag, frames[0].Tag);
        var ping = Serializer.Deserialize<HeartbeatPing>(new MemoryStream(frames[0].Payload));
        Assert.Equal(2, ping.LastStreamIdReceived); // LoginResponse = 1, DataMessageStanza = 2
    }

    /// <summary>
    /// The MCS version byte accompanies ONLY the initial LoginRequest frame; every later client
    /// frame is bare [tag][size][payload]. (The reference JS ports never hit this because they
    /// never send a second client frame at all.)
    /// </summary>
    [Fact]
    public void BuildClientFrame_LoginRequest_IncludesVersionByte()
    {
        var frame = RustPlusFcmSocket.BuildClientFrame(McsProtoTag.KLoginRequestTag, [0x0A]);
        Assert.Equal(new byte[]
        {
            KMcsVersion, (byte)(int)McsProtoTag.KLoginRequestTag, 1, 0x0A
        }, frame);
    }

    /// <summary>See <see cref="BuildClientFrame_LoginRequest_IncludesVersionByte"/>.</summary>
    [Fact]
    public void BuildClientFrame_NonLoginPacket_OmitsVersionByte()
    {
        var frame = RustPlusFcmSocket.BuildClientFrame(McsProtoTag.KHeartbeatPingTag, [0x08, 0x01]);
        Assert.Equal(new byte[]
        {
            (byte)(int)McsProtoTag.KHeartbeatPingTag, 2, 0x08, 0x01
        }, frame);
    }

    [Fact]
    public async Task CloseTag_RaisesSocketClosedAndDisconnects()
    {
        await using var socket = NewSocket();
        var socketClosed = false;
        var disconnected = false;
        socket.SocketClosed += (_, _) => socketClosed = true;
        socket.Disconnected += (_, _) => disconnected = true;

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        await socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script));

        Assert.True(socketClosed);
        Assert.True(disconnected);
    }

    [Fact]
    public async Task UnsupportedVersion_ThrowsInvalidOperationException()
    {
        await using var socket = NewSocket();

        var script = Build(FirstFrame(37, McsProtoTag.KLoginResponseTag, new LoginResponse()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script)));
        Assert.Contains("unsupported", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FirstMessageNotLoginResponse_ThrowsInvalidOperationException()
    {
        await using var socket = NewSocket();

        var script = Build(FirstFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script)));
        Assert.Contains(nameof(LoginResponse), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DataMessageMissingChannelId_IsIgnored()
    {
        await using var socket = NewSocket();
        var raised = false;
        socket.NotificationReceived += (_, _) => raised = true;

        var stanza = new DataMessageStanza
        {
            From = "1",
            PersistentId = "p-missing",
            AppDatas =
            {
                new AppData
                {
                    Key = "body", Value = "{}"
                }
            } // no channelId
        };

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, stanza),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        await socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script));

        Assert.False(raised);
    }

    [Fact]
    public async Task DataMessageMissingBody_IsIgnored()
    {
        await using var socket = NewSocket();
        var raised = false;
        socket.NotificationReceived += (_, _) => raised = true;

        var stanza = new DataMessageStanza
        {
            From = "1",
            PersistentId = "p-missing-body",
            AppDatas =
            {
                new AppData
                {
                    Key = "channelId", Value = "pairing"
                }
            } // no body
        };

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, stanza),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        await socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script));

        Assert.False(raised);
    }

    [Fact]
    public async Task DuplicatePersistentId_IsSkipped()
    {
        // The LoginResponse handler clears the dedupe set, so seeding it up front would not survive.
        // Instead send the same PersistentId twice: the first populates the set, the second is skipped.
        await using var socket = NewSocket([]);
        var count = 0;
        socket.NotificationReceived += (_, _) => count++;

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification(persistentId: "dup-1")),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification(persistentId: "dup-1")),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        await socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script));

        Assert.Equal(1, count); // first delivered, duplicate skipped
    }

    [Fact]
    public async Task ReadVarInt32_MultiByteSize_FrameDelivered()
    {
        // Build a DataMessageStanza whose serialized payload length >= 128, so the size varint
        // requires a continuation byte (multi-byte encoding path in ReadVarInt32).
        await using var socket = NewSocket();
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
                new AppData
                {
                    Key = "channelId", Value = "pairing"
                },
                new AppData
                {
                    Key = "body", Value = longBody
                },
            }
        };

        // Confirm the payload actually crosses the 128-byte boundary.
        var payloadBytes = PayloadOf(bigStanza);
        Assert.True(payloadBytes.Length >= 128, $"Expected payload >= 128 bytes, got {payloadBytes.Length}");

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, bigStanza),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        await socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script));

        Assert.NotNull(notification);
    }

    [Fact]
    public async Task OnGotMessageBytes_CorruptPayload_RaisesErrorOccurred()
    {
        // After a valid LoginResponse, send a frame whose tag maps to DataMessageStanza but whose
        // payload is random bytes that protobuf-net cannot deserialize.  OnGotMessageBytes must
        // catch the exception and fire ErrorOccurred instead of crashing the loop.
        await using var socket = NewSocket();
        Exception? error = null;
        socket.ErrorOccurred += (_, ex) => error = ex;

        // Manually build a frame: [tag][varint(size)][corrupt-payload]
        var corruptPayload = new byte[]
        {
            0xFF, 0xFE, 0xFD, 0xFC, 0xFB, 0xFA, 0xF9
        };
        var corruptFrame =
            new byte[]
                {
                    (byte)(int)McsProtoTag.KDataMessageStanzaTag
                }
                .Concat(EncodeVarInt32(corruptPayload.Length))
                .Concat(corruptPayload);

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            corruptFrame,
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        await socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script));

        Assert.NotNull(error);
    }

    [Fact]
    public async Task IqStanza_IsIgnored_NoNotificationAndNoThrow()
    {
        // The KIqStanzaTag case in OnMessage just breaks — assert no crash and no notification.
        await using var socket = NewSocket();
        var raised = false;
        socket.NotificationReceived += (_, _) => raised = true;

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KIqStanzaTag, new IqStanza()),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        await socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script));

        Assert.False(raised);
    }

    [Fact]
    public async Task UnrecognizedTag_HeartbeatAck_IsIgnored_NoNotificationAndNoThrow()
    {
        // HeartbeatAck is a known protobuf type but has no explicit handling in OnMessage,
        // so it falls through to the default arm (Logger.LogUnrecognizedTag + ignore).
        await using var socket = NewSocket();
        var raised = false;
        socket.NotificationReceived += (_, _) => raised = true;

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KHeartbeatAckTag, new HeartbeatAck()),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        await socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script));

        Assert.False(raised);
    }

    // ── OnDataMessage optional-field branches ────────────────────────────────

    /// <summary>
    /// Sends a DataMessageStanza with ALL optional AppData fields present (title, projectId,
    /// experienceId, scopeKey, message). Covers the "not-null" arm of every <c>??</c> null-
    /// coalescing operator in <c>OnDataMessage</c>.
    /// </summary>
    [Fact]
    public async Task DataMessage_AllOptionalFields_Present_Delivered()
    {
        await using var socket = NewSocket([]);
        string? notification = null;
        socket.NotificationReceived += (_, n) => notification = n;

        var stanza = new DataMessageStanza
        {
            From = "123456789",
            PersistentId = "opt-full",
            Sent = 1_700_000_000_000,
            AppDatas =
            {
                new AppData
                {
                    Key = "channelId", Value = "pairing"
                },
                new AppData
                {
                    Key = "body", Value = "{}"
                },
                new AppData
                {
                    Key = "title", Value = "Test Title"
                },
                new AppData
                {
                    Key = "projectId", Value = "00000000-0000-0000-0000-000000000001"
                },
                new AppData
                {
                    Key = "experienceId", Value = "@scope/exp"
                },
                new AppData
                {
                    Key = "scopeKey", Value = "myScope"
                },
                new AppData
                {
                    Key = "message", Value = "hello world"
                },
            }
        };

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, stanza),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        await socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script));

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
    public async Task DataMessage_NullPersistentId_DeliveredAndNotAddedToDedupeSet()
    {
        var ids = new List<string>();
        await using var socket = NewSocket(ids);
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
                new AppData
                {
                    Key = "channelId", Value = "pairing"
                },
                new AppData
                {
                    Key = "body", Value = "{}"
                },
            }
        };

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, stanza),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        await socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script));

        Assert.Equal(1, count); // message was delivered
        Assert.Empty(ids); // nothing added to the dedupe set
    }

    /// <summary>
    /// Sends a DataMessageStanza with <c>Sent = null</c>.  Covers the
    /// <c>dataMessage.Sent ?? 0</c> null branch (SentAt falls back to epoch).
    /// </summary>
    [Fact]
    public async Task DataMessage_NullSent_FallsBackToEpoch()
    {
        await using var socket = NewSocket([]);
        string? notification = null;
        socket.NotificationReceived += (_, n) => notification = n;

        var stanza = new DataMessageStanza
        {
            From = "123456789",
            PersistentId = "sent-null",
            // Sent deliberately not set (null)
            AppDatas =
            {
                new AppData
                {
                    Key = "channelId", Value = "pairing"
                },
                new AppData
                {
                    Key = "body", Value = "{}"
                },
            }
        };

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, stanza),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        await socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script));

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
    public async Task DataMessage_NoNotificationReceivedSubscriber_DoesNotThrow()
    {
        await using var socket = NewSocket([]);
        // NotificationReceived intentionally NOT subscribed

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification("no-sub")),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        var ex = await Record.ExceptionAsync(() => socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script)));
        Assert.Null(ex);
    }

    /// <summary>
    /// Drives <c>OnGotMessageBytes</c> with an empty payload (data.Length == 0).
    /// This covers the early-return path that calls <c>Activator.CreateInstance</c>
    /// instead of deserializing.
    /// </summary>
    [Fact]
    public async Task EmptyPayload_OnGotMessageBytes_DispatchesDefaultInstance()
    {
        // Manually build a HeartbeatAck frame with a zero-length payload (size varint = 0)
        // after the LoginResponse, then close.
        await using var socket = NewSocket();
        var raised = false;
        socket.NotificationReceived += (_, _) => raised = true;

        var emptyFrame =
            new byte[]
                {
                    (byte)(int)McsProtoTag.KHeartbeatAckTag
                }
                .Concat(EncodeVarInt32(0)); // zero-length payload

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            emptyFrame,
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        var ex = await Record.ExceptionAsync(() => socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script)));

        Assert.Null(ex);
        Assert.False(raised); // HeartbeatAck hits the default arm → ignored, no notification
    }

    /// <summary>
    /// Sends a corrupt payload with NO <see cref="RustPlusFcmSocket.ErrorOccurred"/> subscriber.
    /// Covers the <c>ErrorOccurred?.Invoke</c> null-conditional no-subscriber false-branch
    /// in <c>OnGotMessageBytes</c>'s catch block.
    /// </summary>
    [Fact]
    public async Task OnGotMessageBytes_CorruptPayload_NoErrorSubscriber_DoesNotThrow()
    {
        await using var socket = NewSocket();
        // ErrorOccurred intentionally NOT subscribed

        var corruptPayload = new byte[]
        {
            0xFF, 0xFE, 0xFD, 0xFC
        };
        var corruptFrame =
            new byte[]
                {
                    (byte)(int)McsProtoTag.KDataMessageStanzaTag
                }
                .Concat(EncodeVarInt32(corruptPayload.Length))
                .Concat(corruptPayload);

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            corruptFrame,
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        var ex = await Record.ExceptionAsync(() => socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script)));

        Assert.Null(ex); // catch absorbs exception; no subscriber → no rethrow
    }

    /// <summary>
    /// Drives <c>OnDataMessage</c> with a null DataMessageStanza reference (i.e. the
    /// <c>e.Object as DataMessageStanza</c> cast returns null). This covers the
    /// <c>dataMessage?.PersistentId != null</c> outer null-check false-branch and the
    /// <c>dataMessage?.AppDatas is not { Count: &gt; 0 }</c> null-propagation path.
    /// </summary>
    [Fact]
    public async Task DataMessage_NullStanza_IsIgnoredWithoutThrow()
    {
        // Send a tag that maps to DataMessageStanza but deserializes as an empty/null stanza
        // by sending a zero-length payload — Serializer produces a default instance with
        // null AppDatas list, which matches the "no AppDatas" guard.
        await using var socket = NewSocket();
        var raised = false;
        socket.NotificationReceived += (_, _) => raised = true;

        var zeroLengthDataFrame =
            new byte[]
                {
                    (byte)(int)McsProtoTag.KDataMessageStanzaTag
                }
                .Concat(EncodeVarInt32(0));

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            zeroLengthDataFrame,
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        await socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script));

        Assert.False(raised);
    }

    /// <summary>
    /// Covers the <c>persistentIds == null</c> arm of <c>OnDataMessage</c>'s dedupe guard.
    /// When no <c>persistentIds</c> collection is provided the null-conditional
    /// <c>persistentIds?.Contains(...)</c> returns null, short-circuiting the condition.
    /// </summary>
    [Fact]
    public async Task DataMessage_NullPersistentIdsCollection_DeliversSameMessageTwice()
    {
        // persistentIds = null means no de-duplication at all.
        await using var socket = NewSocket(persistentIds: null);
        var count = 0;
        socket.NotificationReceived += (_, _) => count++;

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification("same-id")),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification("same-id")),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        await socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script));

        Assert.Equal(2, count); // both delivered; no deduplication without the set
    }

    /// <summary>
    /// Asserts that LoginResponse clears the persistentIds collection, so a message whose
    /// persistent ID was known BEFORE login is redelivered after login — kills the
    /// Statement mutation that removes <c>persistentIds?.Clear()</c> in the LoginResponse arm.
    /// </summary>
    [Fact]
    public async Task LoginResponse_ClearsPreSeededPersistentIds()
    {
        // Pre-seed the set with a known ID so that, WITHOUT clearing, the second delivery
        // would be skipped by the dedup check.
        var ids = new List<string>
        {
            "pre-existing-id"
        };
        await using var socket = NewSocket(ids);
        var count = 0;
        socket.NotificationReceived += (_, _) => count++;

        var script = Build(
            // LoginResponse should clear the set (removing "pre-existing-id").
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            // This message has the same ID — it should be DELIVERED because the set was cleared.
            NextFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification(persistentId: "pre-existing-id")),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        await socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script));

        Assert.Equal(1, count);
    }

    /// <summary>
    /// Sends a DataMessageStanza whose body JSON uses lowercase property names (matching the live
    /// FCM format), and asserts that Body fields are correctly deserialized.  The
    /// <see cref="System.Text.Json.JsonSerializerOptions"/> in <see cref="RustPlusFcmSocket"/> use
    /// <c>PropertyNameCaseInsensitive = true</c>; this test fails when that flag is mutated to
    /// <c>false</c>, killing that Boolean mutation survivor.
    /// </summary>
    [Fact]
    public async Task DataMessage_LowerCaseBodyJson_DeserializesBodyFieldsCorrectly()
    {
        await using var socket = NewSocket([]);
        string? notification = null;
        socket.NotificationReceived += (_, n) => notification = n;

        // Body JSON with lowercase keys — requires PropertyNameCaseInsensitive = true to bind
        // "ip" → Body.Ip, "port" → Body.Port, "type" → Body.Type, etc.
        const string lowerCaseBody = """
                                     {"ip":"10.0.0.1","port":28082,"type":"server","playerToken":"42","playerId":"7"}
                                     """;

        var stanza = new DataMessageStanza
        {
            From = "123456789",
            PersistentId = "case-test",
            Sent = 1_700_000_000_000,
            AppDatas =
            {
                new AppData
                {
                    Key = "channelId", Value = "pairing"
                },
                new AppData
                {
                    Key = "body", Value = lowerCaseBody
                },
            }
        };

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, stanza),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        await socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script));

        Assert.NotNull(notification);
        using var doc = System.Text.Json.JsonDocument.Parse(notification!);
        // If PropertyNameCaseInsensitive = false, these deserialized Body fields would be default
        // (null / 0) even though the JSON contained lowercase keys.
        var bodyEl = doc.RootElement.GetProperty("Data").GetProperty("Body");
        Assert.Equal("10.0.0.1", bodyEl.GetProperty("Ip").GetString());
        // Body has [JsonNumberHandling(AllowReadingFromString|WriteAsString)], so Port is serialized
        // back as a JSON string "28082" rather than the number 28082.
        Assert.Equal("28082", bodyEl.GetProperty("Port").GetString());
        Assert.Equal("server", bodyEl.GetProperty("Type").GetString());
    }

    /// <summary>
    /// Asserts that the initial LoginResponse frame IS dispatched via OnGotMessageBytes —
    /// killing the Statement mutation that removes that call at L219.  The side-effect of
    /// dispatching LoginResponse is that it clears <c>persistentIds</c>, which is observable.
    /// </summary>
    [Fact]
    public async Task LoginResponse_DispatchedViaOnGotMessageBytes_ClearsPersistentIds()
    {
        var ids = new List<string>
        {
            "old-id"
        };
        await using var socket = NewSocket(ids);
        var count = 0;
        socket.NotificationReceived += (_, _) => count++;

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            NextFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification("old-id")),
            NextFrame(McsProtoTag.KCloseTag, new Close()));

        await socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script));

        // If OnGotMessageBytes was NOT called for LoginResponse, persistentIds would NOT be cleared,
        // "old-id" would still be in the set, and the DataMessage would be skipped (count == 0).
        Assert.Equal(1, count);
    }

    /// <summary>
    /// Sends an empty-payload HeartbeatPing and asserts exactly ONE HeartbeatAck is written
    /// back to the stream — kills the Statement mutation that removes the early <c>return;</c>
    /// after the Activator.CreateInstance dispatch in <c>OnGotMessageBytes</c>.
    /// </summary>
    /// <remarks>
    /// Without the return, the code falls through to <c>Serializer.NonGeneric.Deserialize</c>
    /// (from an empty stream, producing another default HeartbeatPing), calls <c>OnMessage</c>
    /// a second time, and <c>HandlePing</c> writes a second HeartbeatAck.
    /// </remarks>
    [Fact]
    public async Task EmptyPayload_HeartbeatPing_WritesExactlyOneAck()
    {
        await using var socket = NewSocket();

        // HeartbeatPing with zero-length payload (Activator.CreateInstance returns default instance
        // with null StreamId).
        var emptyPingFrame =
            new byte[]
                {
                    (byte)(int)McsProtoTag.KHeartbeatPingTag
                }
                .Concat(EncodeVarInt32(0)); // zero-length payload

        var stream = new ScriptedStream(Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            emptyPingFrame,
            NextFrame(McsProtoTag.KCloseTag, new Close())));

        await socket.RunReceiveLoopOverStreamAsync(stream);

        // If return; is removed, HandlePing is called twice → two HeartbeatAck writes.
        var ackCount = ParseClientFrames(stream.Writes.ToArray())
            .Count(static frame => frame.Tag == (int)McsProtoTag.KHeartbeatAckTag);

        Assert.Equal(1, ackCount);
    }

    /// <summary>
    /// In production nothing awaits the receive-loop task, so a fault that only propagates out of
    /// the task is invisible — the listener hangs forever. Any unexpected fault (here: an MCS tag
    /// with no protobuf mapping) must therefore ALSO be surfaced via <see cref="RustPlusFcmSocket.ErrorOccurred"/>.
    /// </summary>
    [Fact]
    public async Task ReceiveLoop_UnknownTag_RaisesErrorOccurred()
    {
        await using var socket = NewSocket();
        Exception? error = null;
        socket.ErrorOccurred += (_, ex) => error = ex;

        // KMessageStanzaTag has no BuildProtobufFromTag mapping → the loop faults.
        var unknownTagFrame =
            new byte[]
                {
                    (byte)(int)McsProtoTag.KMessageStanzaTag
                }
                .Concat(EncodeVarInt32(0));

        var script = Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            unknownTagFrame);

        // The fault still propagates to direct awaiters (the seam); production relies on the event.
        var thrown =
            await Record.ExceptionAsync(() => socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script)));

        Assert.NotNull(thrown);
        Assert.NotNull(error);
        Assert.Same(thrown, error);
    }

    /// <summary>
    /// The login-handshake validation failure must likewise reach <see cref="RustPlusFcmSocket.ErrorOccurred"/>,
    /// not just the (unobserved-in-production) receive-loop task.
    /// </summary>
    [Fact]
    public async Task ReceiveLoop_WrongLoginResponse_RaisesErrorOccurred()
    {
        await using var socket = NewSocket();
        Exception? error = null;
        socket.ErrorOccurred += (_, ex) => error = ex;

        var script = Build(FirstFrame(McsProtoTag.KDataMessageStanzaTag, RustNotification()));

        await Record.ExceptionAsync(() => socket.RunReceiveLoopOverStreamAsync(new ScriptedStream(script)));

        Assert.NotNull(error);
        Assert.IsType<InvalidOperationException>(error);
    }

    [Fact]
    public async Task ReceiveLoop_StreamEndsBetweenFrames_ExitsCleanly()
    {
        await using var socket = NewSocket();

        // Login frame only — no Close. After processing login, the loop's tag read hits EOF
        // (ReadByte returns -1) and must break cleanly instead of treating -1 as a tag.
        var stream = new ScriptedStream(Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse())));

        var exception = await Record.ExceptionAsync(() => socket.RunReceiveLoopOverStreamAsync(stream));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ReceiveLoop_TruncatedVarIntSize_ExitsCleanly()
    {
        await using var socket = NewSocket();

        // Login frame, then a lone tag byte with no size varint before EOF. ReadVarInt32 hits EOF
        // and throws EndOfStreamException, which the receive loop swallows for a clean exit
        // (rather than spinning forever on a -1 → 0xFF continuation byte).
        var loneTagByte = Enumerable.Repeat((byte)(int)McsProtoTag.KDataMessageStanzaTag, 1);
        var stream = new ScriptedStream(Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            loneTagByte));

        var exception = await Record.ExceptionAsync(() => socket.RunReceiveLoopOverStreamAsync(stream));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ReceiveLoop_TruncatedPayload_ExitsCleanly()
    {
        await using var socket = NewSocket();

        // Login frame, then a tag + size=10 varint but only 3 payload bytes before EOF. Read hits
        // EOF (underlying Read returns 0) and throws EndOfStreamException rather than busy-looping
        // forever; the receive loop swallows it for a clean exit.
        var truncatedFrame =
            new byte[]
                {
                    (byte)(int)McsProtoTag.KDataMessageStanzaTag
                }
                .Concat(EncodeVarInt32(10))
                .Concat(new byte[]
                {
                    1, 2, 3
                });

        var stream = new ScriptedStream(Build(
            FirstFrame(McsProtoTag.KLoginResponseTag, new LoginResponse()),
            truncatedFrame));

        var exception = await Record.ExceptionAsync(() => socket.RunReceiveLoopOverStreamAsync(stream));

        Assert.Null(exception);
    }

    /// <summary>Concrete subclass: <see cref="RustPlusFcmSocket"/> is abstract.</summary>
    /// <param name="credentials">The FCM credentials.</param>
    /// <param name="persistentIds">The de-duplication set of already-seen persistent ids.</param>
    /// <param name="options">Optional heartbeat/watchdog tuning.</param>
    private sealed class TestSocket(
        Credentials credentials,
        ICollection<string>? persistentIds = null,
        RustPlusFcmSocketOptions? options = null)
        : RustPlusFcmSocket(credentials, persistentIds, options);

    /// <summary>
    /// A duplex stream whose reads are served from a pre-built script and whose writes are captured.
    /// The script always ends with a Close frame so the receive loop terminates deterministically;
    /// the Close handler cancels the token, exiting the <c>while</c> on the next iteration.
    /// </summary>
    /// <param name="script">The pre-built MCS byte script served to reads.</param>
    private sealed class ScriptedStream(byte[] script) : Stream
    {
        private readonly TaskCompletionSource<bool> _firstWrite =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly MemoryStream _reads = new(script);

        public MemoryStream Writes { get; } = new();

        /// <summary>Completes once at least one frame has been written — lets tests wait on the
        /// actual condition instead of a fixed delay that flakes under parallel runs.</summary>
        public Task FirstWrite => _firstWrite.Task;

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _reads.Read(buffer, offset, count);
        public override int ReadByte() => _reads.ReadByte();

        public override void Write(byte[] buffer, int offset, int count)
        {
            Writes.Write(buffer, offset, count);
            _firstWrite.TrySetResult(true);
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    }

    /// <summary>
    /// A duplex stream that ONLY supports asynchronous I/O: synchronous <see cref="Read"/>/<see cref="ReadByte"/>/
    /// <see cref="Write"/> throw. Driving the receive/send path over it proves the loop uses ReadAsync/WriteAsync
    /// (i.e. does not occupy a thread-pool thread with blocking calls for the connection's lifetime).
    /// </summary>
    /// <param name="script">The pre-built MCS byte script served to async reads.</param>
#pragma warning disable CA1844 // memory-based overrides are a perf hint, irrelevant for this test stub
    private sealed class AsyncOnlyStream(byte[] script) : Stream
    {
        private readonly MemoryStream _reads = new(script);
        public MemoryStream Writes { get; } = new();

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("synchronous Read is not allowed");

        public override int ReadByte() => throw new NotSupportedException("synchronous ReadByte is not allowed");

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("synchronous Write is not allowed");

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _reads.ReadAsync(buffer, offset, count, cancellationToken);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Writes.WriteAsync(buffer, offset, count, cancellationToken);

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    }
#pragma warning restore CA1844
}
