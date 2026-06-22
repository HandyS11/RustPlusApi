using McsProto;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ProtoBuf;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;
using RustPlusApi.Fcm.Interfaces;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using static RustPlusApi.Fcm.Data.Tags;
using static RustPlusApi.Fcm.Utils.McsUtils;
using static System.GC;

namespace RustPlusApi.Fcm;

/// <summary>
/// Represents a RustPlus FCM listener client for handling FCM connections and notifications.
/// </summary>
/// <param name="credentials">The <see cref="Credentials"/> used for authentication.</param>
/// <param name="persistentIds">Already-processed message IDs, used for de-duplication. Every data
/// message is checked against and appended to this collection, so for a long-lived listener prefer
/// a set-like implementation (e.g. <see cref="HashSet{T}"/>) — with a <see cref="List{T}"/> the
/// duplicate check is a linear scan that degrades as the collection grows unboundedly.</param>
/// <param name="options">Tuning options (heartbeat interval, inactivity timeout); defaults are used when <see langword="null"/>.</param>
/// <param name="loggerFactory">Routes the client's diagnostics into your logging stack; logging is
/// disabled (a no-op <c>NullLogger</c>) when <see langword="null"/>.</param>
public abstract class RustPlusFcmSocket(
    Credentials credentials,
    ICollection<string>? persistentIds = null,
    RustPlusFcmSocketOptions? options = null,
    ILoggerFactory? loggerFactory = null)
    : IRustPlusFcmSocket
{
    private const string Host = "mtalk.google.com";
    private const int Port = 5228;

    private const int KMcsVersion = 41;

    /// <summary>MCS StreamAck extension id (Chromium <c>mcs_client</c> kStreamAck).</summary>
    private const int KStreamAckExtensionId = 13;

    /// <summary>Bounds teardown waits so a wedged receive loop cannot hang disposal.</summary>
    private static readonly TimeSpan TeardownTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The client's logger; <c>NullLogger</c> when no factory was supplied.
    /// Exposed to derived classes (e.g. <see cref="RustPlusFcm"/>) so they log through the same categorised sink.</summary>
    private protected readonly ILogger Logger =
        (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger("RustPlusApi.Fcm.RustPlusFcmSocket");

    private readonly CancellationTokenSource _cancellationTokenSource = new();

    /// <summary>Tuning values for this instance — a private snapshot taken at construction, so later
    /// mutation of the caller's (possibly shared) options object cannot affect a live socket.</summary>
    private readonly RustPlusFcmSocketOptions _options = options?.Clone() ?? new RustPlusFcmSocketOptions();

    private readonly JsonSerializerOptions _parsingOptions = new()
    {
        PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Serializes writes to the transport so concurrent sends (e.g. a ping-ack racing another
    /// send) cannot interleave bytes on the stream.</summary>
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private Task? _heartbeatLoop;

    /// <summary>UTC tick count of the last received frame, observed by the inactivity watchdog.
    /// Stored as ticks behind <see cref="Volatile"/> because the watchdog reads it from another
    /// thread, and a non-volatile 64-bit read can tear on 32-bit netstandard2.0 hosts.</summary>
    private long _lastTrafficTicks = DateTime.UtcNow.Ticks;

    private Task? _receiveLoop;
    private SslStream? _sslStream;

    /// <summary>Incoming MCS stream position: the count of frames received since login (the
    /// LoginResponse is 1). Reported back to the server as <c>LastStreamIdReceived</c> on stream
    /// acks and heartbeats — without it the server treats delivered messages as unacknowledged,
    /// closes the connection after a few minutes and redelivers them on the next connect.</summary>
    private int _streamIdIn;

    private TcpClient? _tcpClient;

    /// <summary>The transport stream used for reading and writing MCS frames.
    /// In production this is set to the authenticated <see cref="SslStream"/> immediately after
    /// TLS handshake; tests supply an in-memory stream via <see cref="RunReceiveLoopOverStreamAsync"/>.</summary>
    private Stream? _transport;

    /// <summary>Tear-free accessor over <see cref="_lastTrafficTicks"/>.</summary>
    private DateTime LastTrafficUtc
    {
        get => new(Volatile.Read(ref _lastTrafficTicks), DateTimeKind.Utc);
        set => Volatile.Write(ref _lastTrafficTicks, value.Ticks);
    }

    private CancellationToken CancellationToken => _cancellationTokenSource.Token;

    /// <summary>
    /// Occurs when the client is starting to connect to the FCM server.
    /// </summary>
    public event EventHandler? Connecting;

    /// <summary>
    /// Occurs when the client has successfully connected to the FCM server.
    /// </summary>
    public event EventHandler? Connected;

    /// <summary>
    /// Occurs when a notification is received.
    /// </summary>
    /// <remarks>
    /// The event data is the notification as a <see cref="string"/>.
    /// </remarks>
    public event EventHandler<string>? NotificationReceived;

    /// <summary>
    /// Occurs when the client is disconnecting from the FCM server.
    /// </summary>
    public event EventHandler? Disconnecting;

    /// <summary>
    /// Occurs when the client has disconnected from the FCM server.
    /// </summary>
    public event EventHandler? Disconnected;

    /// <summary>
    /// Occurs when the socket is closed.
    /// </summary>
    public event EventHandler? SocketClosed;

    /// <summary>
    /// Occurs when an error is encountered.
    /// </summary>
    /// <remarks>
    /// The event data is the <see cref="Exception"/> that was thrown.
    /// </remarks>
    public event EventHandler<Exception>? ErrorOccurred;

    /// <summary>
    /// Occurs once for each newly-harvested FCM <c>persistentId</c>, immediately after it is added to
    /// the tracked set. Subscribe to persist ids incrementally so a crash or quick restart cannot
    /// reopen the redelivery window (the server only stops redelivering a message once its id is
    /// replayed in a later login's <c>ReceivedPersistentIds</c>).
    /// </summary>
    /// <remarks>The event data is the harvested <c>persistentId</c> as a <see cref="string"/>.</remarks>
    public event EventHandler<string>? PersistentIdReceived;

    /// <summary>
    /// A snapshot of the FCM <c>persistentId</c>s currently tracked for de-duplication — the ids
    /// supplied at construction plus every id harvested since. Persist these and pass them back into
    /// a new instance to suppress redelivery of already-processed messages across reconnects. The
    /// collection is never <see langword="null"/> (empty when no ids are tracked). Ids have a
    /// server-side lifespan; pruning your persisted copy is the caller's responsibility.
    /// </summary>
    public IReadOnlyCollection<string> PersistentIds =>
        persistentIds is null ? [] : [.. persistentIds];

    /// <summary>
    /// Connects to the FCM MCS server over TLS, performs the MCS login handshake,
    /// and starts the background message-receive loop.
    /// On failure, <see cref="ErrorOccurred"/> is raised, the partial transport is released (so the
    /// instance can retry), and the exception is rethrown to the caller.
    /// Instances are single-connection: after <see cref="Disconnect"/> or disposal, create a new
    /// instance to reconnect.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the connection attempt (TLS connect on net10.0+).</param>
    /// <exception cref="InvalidOperationException">Thrown when the socket is already connected, or was closed by <see cref="Disconnect"/>/disposal.</exception>
    /// <remarks>Excluded from coverage: live TLS connection to mtalk.google.com:5228;
    /// the MCS pipeline it drives is exercised offline via the <c>RunReceiveLoopOverStreamAsync</c> seam.</remarks>
    [ExcludeFromCodeCoverage]
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_tcpClient is not null || _cancellationTokenSource.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "This socket has already been connected or closed; instances are single-connection — create a new instance to reconnect.");
        }

        Connecting?.Invoke(this, EventArgs.Empty);

        try
        {
            _tcpClient = new TcpClient();
#if NET10_0_OR_GREATER
            // Either the caller's token or the instance token can cancel the TLS connect.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, cancellationToken);
            await _tcpClient.ConnectAsync(Host, Port, linked.Token).ConfigureAwait(false);
#else
            // netstandard2.0 lacks the CancellationToken overload.
            await _tcpClient.ConnectAsync(Host, Port).ConfigureAwait(false);
#endif

            _sslStream = new SslStream(_tcpClient.GetStream(), false);
            await _sslStream.AuthenticateAsClientAsync(Host).ConfigureAwait(false);
            _transport = _sslStream;

            var loginRequest = new LoginRequest
            {
                AdaptiveHeartbeat = false,
                auth_service = LoginRequest.AuthService.AndroidId,
                AuthToken = credentials.Gcm.SecurityToken.ToString(CultureInfo.InvariantCulture),
                Id = "chrome-63.0.3234.0",
                Domain = "mcs.android.com",
                DeviceId =
                    $"android-{BigInteger.Parse(credentials.Gcm.AndroidId.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture):X}",
                NetworkType = 1,
                Resource = credentials.Gcm.AndroidId.ToString(CultureInfo.InvariantCulture),
                User = credentials.Gcm.AndroidId.ToString(CultureInfo.InvariantCulture),
                UseRmq2 = true,
                Settings =
                {
                    new Setting
                    {
                        Name = "new_vc", Value = "1"
                    }
                },
                ClientEvents =
                {
                    new ClientEvent()
                },
            };

            if (persistentIds != null)
            {
                loginRequest.ReceivedPersistentIds.AddRange(persistentIds);
            }

            await SendPacketAsync(loginRequest).ConfigureAwait(false);

            Connected?.Invoke(this, EventArgs.Empty);

            // Truly async: the loop yields at the first awaited read instead of holding a thread-pool thread.
            LastTrafficUtc = DateTime.UtcNow;
            _receiveLoop = ReceiveMessagesAsync();

            // Client-initiated heartbeat + inactivity watchdog: a NAT that silently drops the idle
            // mapping would otherwise leave the blocking read waiting forever with no error.
            _heartbeatLoop = RunHeartbeatLoopAsync();
        }
        catch (Exception ex)
        {
            // Surface the failure on the event *and* to the caller, and release the partial transport
            // so a failed connect leaves the instance clean (and retryable).
#pragma warning disable CA1849, VSTHRD103, S6966 // sync Dispose is intentional (ns2.0 has no Stream.DisposeAsync)
            _sslStream?.Dispose();
#pragma warning restore CA1849, VSTHRD103, S6966
            _tcpClient?.Dispose();
            _sslStream = null;
            _tcpClient = null;
            _transport = null;

            Logger.LogConnectFailed(ex);
            ErrorOccurred?.Invoke(this, ex);
            throw;
        }
    }

    /// <summary>
    /// Disconnects the client from the FCM server and releases associated resources.
    /// </summary>
    /// <remarks>
    /// Invokes the <see cref="Disconnecting"/> and <see cref="Disconnected"/> events.
    /// </remarks>
    public void Disconnect()
    {
        Disconnecting?.Invoke(this, EventArgs.Empty);

        _cancellationTokenSource.Cancel();

        _sslStream?.Close();
        _tcpClient?.Close();

        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Releases resources used by the <see cref="RustPlusFcmSocket"/>.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        SuppressFinalize(this);
    }

    /// <summary>
    /// Asynchronously disposes the socket: cancels background work, unblocks the in-progress read by
    /// tearing down the transport, then awaits the tracked receive loop (bounded by <see cref="TeardownTimeout"/>)
    /// before releasing remaining resources. Prefer this over <see cref="Dispose()"/> so teardown
    /// deterministically drains the receive loop instead of abandoning it.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await DisposeCoreAsync().ConfigureAwait(false);
        SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the resources used by the <see cref="RustPlusFcmSocket"/>.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> to release both managed and unmanaged resources;
    /// <see langword="false"/> to release only unmanaged resources.
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        if (!_cancellationTokenSource.IsCancellationRequested)
        {
            _cancellationTokenSource.Cancel();
        }

        _sslStream?.Dispose();
        _tcpClient?.Dispose();
        _cancellationTokenSource.Dispose();
        _sendLock.Dispose();
    }

    /// <summary>
    /// Cancels the instance token, tears down the transport to unblock the synchronous read, awaits the
    /// tracked receive loop (bounded), then disposes remaining resources. Override to extend async teardown.
    /// </summary>
    protected virtual async ValueTask DisposeCoreAsync()
    {
        if (!_cancellationTokenSource.IsCancellationRequested)
        {
#if NET10_0_OR_GREATER
            await _cancellationTokenSource.CancelAsync().ConfigureAwait(false);
#else
            _cancellationTokenSource.Cancel();
#endif
        }

        // Tear down the transport so any blocking Read/ReadByte unblocks and the loop can observe EOF.
        // Synchronous Dispose is intentional: it works on both TFMs (netstandard2.0 lacks Stream.DisposeAsync)
        // and a cheap unblock is all that's needed here.
#pragma warning disable CA1849, VSTHRD103, S6966 // sync Dispose is intentional (ns2.0 has no Stream.DisposeAsync)
        _transport?.Dispose();
        _sslStream?.Dispose();
#pragma warning restore CA1849, VSTHRD103, S6966
        _tcpClient?.Dispose();

        await WaitForReceiveLoopAsync().ConfigureAwait(false);

        _cancellationTokenSource.Dispose();
        _sendLock.Dispose();
    }

    /// <summary>
    /// Awaits the tracked receive and heartbeat loops, bounded by <see cref="TeardownTimeout"/>. A fault
    /// is expected when the transport is torn out from under a blocking read, so it is swallowed on teardown.
    /// </summary>
    private async Task WaitForReceiveLoopAsync()
    {
        var loops = new[]
            {
                _receiveLoop, _heartbeatLoop
            }
            .Where(static t => t is not null)
            .Cast<Task>()
            .ToArray();
        if (loops.Length == 0)
        {
            return;
        }

        var all = Task.WhenAll(loops);
        var completed = await Task.WhenAny(all, Task.Delay(TeardownTimeout)).ConfigureAwait(false);
        if (completed != all)
        {
            return;
        }

        try
        {
            await all.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A loop faulted because the transport was disposed mid-read/write: expected during teardown.
            Logger.LogTeardownLoopFaulted(ex);
        }
    }

    /// <summary>Test seam: runs the MCS receive/dispatch loop against an arbitrary stream as a tracked task,
    /// bypassing the live TLS connect, and returns it so tests can await completion or assert teardown awaits
    /// it. Internal — visible only to RustPlusApi.Fcm.UnitTests.</summary>
    /// <param name="stream">The stream to read MCS frames from and write responses to.</param>
    /// <returns>The tracked receive-loop task.</returns>
    internal Task RunReceiveLoopOverStreamAsync(Stream stream)
    {
        _transport = stream;
        _receiveLoop = ReceiveMessagesAsync();
        return _receiveLoop;
    }

    /// <summary>Test seam: runs the heartbeat/watchdog loop against an arbitrary stream as a tracked task,
    /// bypassing the live TLS connect. Internal — visible only to RustPlusApi.Fcm.UnitTests.</summary>
    /// <param name="stream">The stream heartbeat pings are written to.</param>
    /// <returns>The tracked heartbeat-loop task.</returns>
    internal Task RunHeartbeatLoopOverStreamAsync(Stream stream)
    {
        _transport = stream;
        LastTrafficUtc = DateTime.UtcNow;
        _heartbeatLoop = RunHeartbeatLoopAsync();
        return _heartbeatLoop;
    }

    /// <summary>
    /// Periodically sends a client-initiated <see cref="HeartbeatPing"/> and watches for inactivity.
    /// The ping keeps NAT/firewall mappings alive and provokes an ack; if no frame at all arrives for
    /// <see cref="RustPlusFcmSocketOptions.InactivityTimeout"/>, the connection is presumed silently
    /// dead — <see cref="ErrorOccurred"/> is raised with a <see cref="TimeoutException"/> and the
    /// socket is disconnected so the caller can create a fresh instance.
    /// </summary>
    private async Task RunHeartbeatLoopAsync()
    {
        try
        {
            while (!CancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_options.HeartbeatInterval, CancellationToken).ConfigureAwait(false);

                if (DateTime.UtcNow - LastTrafficUtc > _options.InactivityTimeout)
                {
                    var error = new TimeoutException(
                        $"No FCM traffic for {_options.InactivityTimeout.TotalMinutes:0.#} min; the connection is presumed dead.");
                    ErrorOccurred?.Invoke(this, error);
                    Disconnect();
                    return;
                }

                // Piggyback our incoming stream position so the server sees its frames acked even
                // when no StreamAck was sent since (mirrors Chromium's mcs_client heartbeat).
                await SendPacketAsync(new HeartbeatPing
                {
                    LastStreamIdReceived = _streamIdIn
                }).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Disconnect/dispose cancelled the token: clean exit.
        }
        catch (Exception ex)
        {
            // The transport died under a heartbeat write. Surface it unless we're tearing down.
            if (!_cancellationTokenSource.IsCancellationRequested)
            {
                ErrorOccurred?.Invoke(this, ex);
            }
        }
    }

    /// <summary>
    /// Continuously receives and processes messages from the FCM server over the transport stream using
    /// asynchronous I/O. Validates the protocol version and login response, then loops handling incoming messages.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the protocol version is unsupported, or if the initial response is not a <see cref="LoginResponse"/>.
    /// </exception>
    private async Task ReceiveMessagesAsync()
    {
        try
        {
            // Read the header
            var header = await ReadBytesAsync(2).ConfigureAwait(false);
            int version = header[0];
            int tag = header[1];

            if (version is < KMcsVersion and not 38)
            {
                throw new InvalidOperationException($"Protocol version {version} unsupported");
            }

            var size = await ReadVarInt32Async().ConfigureAwait(false);
            var payload = await ReadBytesAsync(size).ConfigureAwait(false);
            var type = BuildProtobufFromTag((McsProtoTag)tag);

            if (type != typeof(LoginResponse))
            {
                throw new InvalidOperationException(
                    $"Got wrong login response. Expected {nameof(LoginResponse)}, got {type.Name}");
            }

            await OnGotMessageBytesAsync(payload, type).ConfigureAwait(false);

            while (!CancellationToken.IsCancellationRequested)
            {
                // Read the tag and size
                tag = await ReadByteAsync().ConfigureAwait(false);
                if (tag < 0)
                {
                    break; // EOF: the server closed the connection between frames.
                }

                size = await ReadVarInt32Async().ConfigureAwait(false);
                payload = await ReadBytesAsync(size).ConfigureAwait(false);
                type = BuildProtobufFromTag((McsProtoTag)tag);

                await OnGotMessageBytesAsync(payload, type).ConfigureAwait(false);
            }
        }
        catch (EndOfStreamException)
        {
            // Stream closed mid-frame (disconnect/truncation): exit the receive loop cleanly
            // rather than hang or surface a confusing decode error.
        }
        catch (OperationCanceledException)
        {
            // Teardown cancelled the token mid-read: a cancelled read is a clean exit, not an error.
        }
        catch (Exception ex)
        {
            // Nothing awaits this task in production, so a fault that only propagates out of it is
            // invisible and the listener hangs forever. Surface it on ErrorOccurred (unless we're
            // tearing down), then rethrow for direct awaiters (tests via the seam).
            if (!_cancellationTokenSource.IsCancellationRequested)
            {
                Logger.LogReceiveLoopFaulted(ex);
                ErrorOccurred?.Invoke(this, ex);
            }

            throw;
        }
    }

    /// <summary>
    /// Deserializes a protobuf message from the given byte array and dispatches it to the message handler.
    /// </summary>
    /// <param name="data">The message bytes.</param>
    /// <param name="type">The type of the protobuf message.</param>
    private async Task OnGotMessageBytesAsync(byte[] data, Type type)
    {
        // Any decoded frame counts as traffic for the inactivity watchdog,
        // and advances the incoming stream position the server expects us to ack.
        LastTrafficUtc = DateTime.UtcNow;
        _streamIdIn++;

        try
        {
            var messageTag = GetTagFromProtobufType(type);

            if (data.Length == 0)
            {
                await OnMessageAsync(new MessageEventArgs
                {
                    Tag = messageTag, Object = Activator.CreateInstance(type)
                }).ConfigureAwait(false);
                return;
            }

#pragma warning disable RCS1261 // MemoryStream.DisposeAsync is a no-op; await using not available in netstandard2.0
            using var stream = new MemoryStream(data);
#pragma warning restore RCS1261
            var message = Serializer.NonGeneric.Deserialize(type, stream);

            await OnMessageAsync(new MessageEventArgs
            {
                Tag = messageTag, Object = message
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
        }
    }

    /// <summary>
    /// Reads the specified number of bytes from the SSL stream.
    /// </summary>
    /// <param name="size">The number of bytes to read.</param>
    /// <returns>A byte array containing the data read from the stream.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the stream closes before <paramref name="size"/> bytes arrive.</exception>
    private async Task<byte[]> ReadBytesAsync(int size)
    {
        var buffer = new byte[size];
        var bytesRead = 0;
        while (bytesRead < size)
        {
            // byte[] overload is intentional: the Memory<byte> overload (preferred by CA1835) is unavailable on netstandard2.0.
#pragma warning disable CA1835
            var read = await _transport!.ReadAsync(buffer, bytesRead, size - bytesRead, CancellationToken)
                .ConfigureAwait(false);
#pragma warning restore CA1835
            if (read == 0)
            {
                throw new EndOfStreamException(); // stream closed before the full frame arrived
            }

            bytesRead += read;
        }

        return buffer;
    }

    /// <summary>
    /// Reads a single byte from the transport asynchronously.
    /// </summary>
    /// <returns>The byte value, or -1 at end of stream.</returns>
    private async Task<int> ReadByteAsync()
    {
        var one = new byte[1];
        // byte[] overload is intentional: the Memory<byte> overload (preferred by CA1835) is unavailable on netstandard2.0.
#pragma warning disable CA1835
        var read = await _transport!.ReadAsync(one, 0, 1, CancellationToken).ConfigureAwait(false);
#pragma warning restore CA1835
        return read == 0 ? -1 : one[0];
    }

    /// <summary>
    /// Reads a variable-length 32-bit integer from the transport.
    /// </summary>
    /// <returns>The decoded 32-bit integer.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the stream closes mid-value.</exception>
    private async Task<int> ReadVarInt32Async()
    {
        var result = 0;
        var shift = 0;
        while (true)
        {
            var b = await ReadByteAsync().ConfigureAwait(false);
            if (b < 0)
            {
                throw new EndOfStreamException(); // stream closed mid-varint
            }

            // Stryker disable once Assignment: each 7-bit group lands in disjoint bit positions, so |= equals ^=.
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                break;
            }

            shift += 7;
        }

        return result;
    }

    /// <summary>
    /// Serializes and sends a protobuf packet over the transport. Writes are serialized through
    /// <see cref="_sendLock"/> so concurrent sends cannot interleave bytes on the stream.
    /// </summary>
    /// <param name="packet">The packet object to serialize and send.</param>
    private async Task SendPacketAsync(object packet)
    {
        var tagEnum = GetTagFromProtobufType(packet.GetType());

#pragma warning disable RCS1261 // MemoryStream.DisposeAsync is a no-op; await using not available in netstandard2.0
        using var ms = new MemoryStream();
#pragma warning restore RCS1261
        Serializer.Serialize(ms, packet);

        var frame = BuildClientFrame(tagEnum, ms.ToArray());

        await _sendLock.WaitAsync(CancellationToken).ConfigureAwait(false);
        try
        {
            // byte[] overload is intentional: the Memory<byte> overload (preferred by CA1835) is unavailable on netstandard2.0.
#pragma warning disable CA1835
            await _transport!.WriteAsync(frame, 0, frame.Length, CancellationToken).ConfigureAwait(false);
#pragma warning restore CA1835
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Frames an MCS packet for the wire. The protocol version byte accompanies ONLY the initial
    /// <see cref="LoginRequest"/>; every later client frame is bare <c>[tag][varint-size][payload]</c>.
    /// A stray version byte on a post-login frame desyncs the server's parser and gets the
    /// connection closed. Internal — visible to RustPlusApi.Fcm.UnitTests.
    /// </summary>
    /// <param name="tag">The MCS tag identifying the packet type.</param>
    /// <param name="payload">The serialized protobuf payload.</param>
    internal static byte[] BuildClientFrame(McsProtoTag tag, byte[] payload)
    {
        byte[] header = tag == McsProtoTag.KLoginRequestTag
            ? [KMcsVersion, (byte)(int)tag]
            : [(byte)(int)tag];

        return [.. header, .. EncodeVarInt32(payload.Length), .. payload];
    }

    /// <summary>
    /// Handles an incoming FCM heartbeat ping by sending a heartbeat acknowledgment that reports
    /// our incoming stream position (<see cref="_streamIdIn"/>) — the MCS contract for telling the
    /// server its frames were received. Outgoing stream ids are implicit (frame count), never set.
    /// </summary>
    /// <param name="ping">The <see cref="HeartbeatPing"/> message received from the server.</param>
    private async Task HandlePingAsync(HeartbeatPing? ping)
    {
        if (ping == null)
        {
            return;
        }

        Logger.LogRespondingToPing(ping.StreamId, ping.LastStreamIdReceived, ping.Status);
        var pingResponse = new HeartbeatAck
        {
            LastStreamIdReceived = _streamIdIn
        };

        await SendPacketAsync(pingResponse).ConfigureAwait(false);
    }

    /// <summary>
    /// Acknowledges received reliable frames with a StreamAck IqStanza carrying
    /// <see cref="_streamIdIn"/>. Sent after every <see cref="DataMessageStanza"/>: without the
    /// ack the server treats the message as undelivered, closes the connection after a few
    /// minutes to force redelivery, and replays the message on the next connect.
    /// </summary>
    private async Task SendStreamAckAsync()
    {
        var streamAck = new IqStanza
        {
            Type = IqStanza.IqType.Set,
            Id = string.Empty,
            Extension = new Extension
            {
                Id = KStreamAckExtensionId, Data = []
            },
            LastStreamIdReceived = _streamIdIn
        };

        await SendPacketAsync(streamAck).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles incoming protocol messages by dispatching them based on their tag.
    /// Unrecognized tags are ignored so the receive loop keeps running.
    /// </summary>
    /// <param name="e">The <see cref="MessageEventArgs"/> containing the message tag and object.</param>
    private async Task OnMessageAsync(MessageEventArgs e)
    {
        // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
        switch (e.Tag)
        {
            case McsProtoTag.KLoginResponseTag:
                persistentIds?.Clear();
                break;
            case McsProtoTag.KDataMessageStanzaTag:
                OnDataMessage(e.Object as DataMessageStanza);
                await SendStreamAckAsync().ConfigureAwait(false);
                break;
            case McsProtoTag.KHeartbeatPingTag:
                await HandlePingAsync(e.Object as HeartbeatPing).ConfigureAwait(false);
                break;
            case McsProtoTag.KCloseTag:
                SocketClosed?.Invoke(this, EventArgs.Empty);
                Disconnect();
                break;
            case McsProtoTag.KIqStanzaTag:
                break; // To investigate further, if needed
            default:
                Logger.LogUnrecognizedTag(e.Tag);
                break;
        }
    }

    /// <summary>
    /// Processes an incoming <see cref="DataMessageStanza"/> by extracting Rust+ notification data,
    /// building an <see cref="FcmMessage"/>, and dispatching it. Skips messages already processed,
    /// and invokes <see cref="ParseNotification"/> and <see cref="NotificationReceived"/>.
    /// </summary>
    /// <param name="dataMessage">The <see cref="DataMessageStanza"/> to process.</param>
    private void OnDataMessage(DataMessageStanza? dataMessage)
    {
        if (dataMessage?.PersistentId != null &&
            persistentIds?.Contains(dataMessage.PersistentId) == true)
        {
            return;
        }

        if (dataMessage?.AppDatas is not { Count: > 0 })
        {
            Logger.LogNoAppData();
            return;
        }

        var appDataDict = dataMessage.AppDatas.ToDictionary(x => x.Key, x => x.Value);

        if (!appDataDict.TryGetValue("channelId", out var channelId) ||
            !appDataDict.TryGetValue("body", out var body))
        {
            Logger.LogNotRustNotification();
            return;
        }

        appDataDict.TryGetValue("title", out var title);
        appDataDict.TryGetValue("projectId", out var projectId);
        appDataDict.TryGetValue("experienceId", out var experienceId);
        appDataDict.TryGetValue("scopeKey", out var scopeKey);
        appDataDict.TryGetValue("message", out var message);

        var bodyData = JsonSerializer.Deserialize<Body>(body, _parsingOptions);

        if (bodyData is null)
        {
            // A JSON-literal null body deserializes to null without throwing; skip it here
            // instead of deferring a NullReferenceException into downstream event handlers.
            Logger.LogNullNotificationBody();
            return;
        }

        var fcmMessage = new FcmMessage
        {
            PersistentId = dataMessage.PersistentId ?? string.Empty,
            From = long.Parse(dataMessage.From, CultureInfo.InvariantCulture),
            SentAt = DateTimeOffset.FromUnixTimeMilliseconds(dataMessage.Sent ?? 0).UtcDateTime,
            Data = new MessageData
            {
                ChannelId = channelId,
                ProjectId = Guid.Parse(projectId ?? Guid.Empty.ToString()),
                Body = bodyData,
                Title = title ?? string.Empty,
                Message = message ?? string.Empty,
                ExperienceId = experienceId ?? string.Empty,
                ScopeKey = scopeKey ?? string.Empty,
            }
        };

        if (dataMessage.PersistentId is not null && persistentIds is not null)
        {
            persistentIds.Add(dataMessage.PersistentId);
            PersistentIdReceived?.Invoke(this, dataMessage.PersistentId);
        }

        ParseNotification(fcmMessage);
        NotificationReceived?.Invoke(this, JsonSerializer.Serialize(fcmMessage));
    }

    /// <summary>
    /// Parses an incoming <see cref="FcmMessage"/> notification.
    /// Override this method in a derived class to handle specific notification logic.
    /// </summary>
    /// <param name="message">The <see cref="FcmMessage"/> to parse.</param>
    protected virtual void ParseNotification(FcmMessage message) { }
}
