using McsProto;
using ProtoBuf;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;
using RustPlusApi.Fcm.Interfaces;
using System.Diagnostics;
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
/// <param name="persistentIds">The collection of persistent IDs as <see cref="ICollection{T}"/> of <see cref="string"/>.</param>
public abstract class RustPlusFcmSocket(Credentials credentials, ICollection<string>? persistentIds = null)
    : IRustPlusFcmSocket, IDisposable, IAsyncDisposable
{
    private const string Host = "mtalk.google.com";
    private const int Port = 5228;

    private const int KMcsVersion = 41;

    /// <summary>Bounds teardown waits so a wedged receive loop cannot hang disposal.</summary>
    private static readonly TimeSpan TeardownTimeout = TimeSpan.FromSeconds(5);

    private TcpClient? _tcpClient;
    private SslStream? _sslStream;

    private Task? _receiveLoop;

    /// <summary>Serializes writes to the transport so concurrent sends (e.g. a ping-ack racing another
    /// send) cannot interleave bytes on the stream.</summary>
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    /// <summary>The transport stream used for reading and writing MCS frames.
    /// In production this is set to the authenticated <see cref="SslStream"/> immediately after
    /// TLS handshake; tests supply an in-memory stream via <see cref="RunReceiveLoopOverStreamAsync"/>.</summary>
    private Stream? _transport;

    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private CancellationToken CancellationToken => _cancellationTokenSource.Token;

    private readonly JsonSerializerOptions _parsingOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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
                DeviceId = $"android-{BigInteger.Parse(credentials.Gcm.AndroidId.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture):X}",
                NetworkType = 1,
                Resource = credentials.Gcm.AndroidId.ToString(CultureInfo.InvariantCulture),
                User = credentials.Gcm.AndroidId.ToString(CultureInfo.InvariantCulture),
                UseRmq2 = true,
                Settings = { new Setting { Name = "new_vc", Value = "1" } },
                ClientEvents = { new ClientEvent() },
            };

            if (persistentIds != null)
            {
                loginRequest.ReceivedPersistentIds.AddRange(persistentIds);
            }

            await SendPacketAsync(loginRequest).ConfigureAwait(false);

            Connected?.Invoke(this, EventArgs.Empty);

            // Truly async: the loop yields at the first awaited read instead of holding a thread-pool thread.
            _receiveLoop = ReceiveMessagesAsync();
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

            Debug.WriteLine($"Exception occured on ConnectAsync: {ex}");
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
    /// Asynchronously disposes the socket: cancels background work, unblocks the in-progress read by
    /// tearing down the transport, then awaits the tracked receive loop (bounded by <see cref="TeardownTimeout"/>)
    /// before releasing remaining resources. Prefer this over <see cref="Dispose()"/> so teardown
    /// deterministically drains the receive loop instead of abandoning it.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        SuppressFinalize(this);
    }

    /// <summary>
    /// Cancels the instance token, tears down the transport to unblock the synchronous read, awaits the
    /// tracked receive loop (bounded), then disposes remaining resources. Override to extend async teardown.
    /// </summary>
    protected virtual async ValueTask DisposeAsyncCore()
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
    /// Awaits the tracked receive loop, bounded by <see cref="TeardownTimeout"/>. A fault is expected when
    /// the transport is torn out from under a blocking read, so it is swallowed on teardown.
    /// </summary>
    private async Task WaitForReceiveLoopAsync()
    {
        var loop = _receiveLoop;
        if (loop is null)
        {
            return;
        }

        var completed = await Task.WhenAny(loop, Task.Delay(TeardownTimeout)).ConfigureAwait(false);
        if (completed != loop)
        {
            return;
        }

        try
        {
#pragma warning disable VSTHRD003 // we own this background task; awaiting it on teardown cannot deadlock
            await loop.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        catch (Exception ex)
        {
            // The loop faulted because the transport was disposed mid-read: expected during teardown.
            Debug.WriteLine($"Receive loop faulted during teardown (expected): {ex}");
        }
    }

    /// <summary>Test seam: runs the MCS receive/dispatch loop against an arbitrary stream as a tracked task,
    /// bypassing the live TLS connect, and returns it so tests can await completion or assert teardown awaits
    /// it. Internal — visible only to RustPlusApi.Tests.</summary>
    /// <param name="stream">The stream to read MCS frames from and write responses to.</param>
    /// <returns>The tracked receive-loop task.</returns>
    internal Task RunReceiveLoopOverStreamAsync(Stream stream)
    {
        _transport = stream;
        _receiveLoop = ReceiveMessagesAsync();
        return _receiveLoop;
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
                throw new InvalidOperationException($"Got wrong login response. Expected {nameof(LoginResponse)}, got {type.Name}");
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
    }

    /// <summary>
    /// Deserializes a protobuf message from the given byte array and dispatches it to the message handler.
    /// </summary>
    /// <param name="data">The message bytes.</param>
    /// <param name="type">The type of the protobuf message.</param>
    private async Task OnGotMessageBytesAsync(byte[] data, Type type)
    {
        try
        {
            var messageTag = GetTagFromProtobufType(type);

            if (data.Length == 0)
            {
                await OnMessageAsync(new MessageEventArgs { Tag = messageTag, Object = Activator.CreateInstance(type) }).ConfigureAwait(false);
                return;
            }

#pragma warning disable RCS1261 // MemoryStream.DisposeAsync is a no-op; await using not available in netstandard2.0
            using var stream = new MemoryStream(data);
#pragma warning restore RCS1261
            var message = Serializer.NonGeneric.Deserialize(type, stream);

            await OnMessageAsync(new MessageEventArgs { Tag = messageTag, Object = message }).ConfigureAwait(false);
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
            var read = await _transport!.ReadAsync(buffer, bytesRead, size - bytesRead, CancellationToken).ConfigureAwait(false);
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
        var header = new byte[] { KMcsVersion, (byte)(int)tagEnum };

#pragma warning disable RCS1261 // MemoryStream.DisposeAsync is a no-op; await using not available in netstandard2.0
        using var ms = new MemoryStream();
#pragma warning restore RCS1261
        Serializer.Serialize(ms, packet);

        var payload = ms.ToArray();
        byte[] frame = [.. header, .. EncodeVarInt32(payload.Length), .. payload];

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
    /// Handles an incoming FCM heartbeat ping by sending a corresponding heartbeat acknowledgment.
    /// </summary>
    /// <param name="ping">The <see cref="HeartbeatPing"/> message received from the server.</param>
    private async Task HandlePingAsync(HeartbeatPing? ping)
    {
        if (ping == null)
        {
            return;
        }

        Debug.WriteLine($"Responding to ping: Stream ID: {ping.StreamId}," +
                        $"Last: {ping.LastStreamIdReceived}," +
                        $"Status: {ping.Status}");
        var pingResponse = new HeartbeatAck
        {
            StreamId = (ping.StreamId ?? 0) + 1,
            LastStreamIdReceived = ping.StreamId,
            Status = ping.Status
        };

        await SendPacketAsync(pingResponse).ConfigureAwait(false);
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
                break;
            case McsProtoTag.KHeartbeatPingTag:
                await HandlePingAsync(e.Object as HeartbeatPing).ConfigureAwait(false);
                break;
            case McsProtoTag.KCloseTag:
                SocketClosed?.Invoke(this, EventArgs.Empty);
                Disconnect();
                break;
            case McsProtoTag.KIqStanzaTag:
                break;  // To investigate further, if needed
            default:
                Debug.WriteLine($"Ignoring unrecognized tag: {e.Tag}");
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
            Debug.WriteLine("⚠️ No AppData found in message");
            return;
        }

        var appDataDict = dataMessage.AppDatas.ToDictionary(x => x.Key, x => x.Value);

        if (!appDataDict.TryGetValue("channelId", out var channelId) ||
            !appDataDict.TryGetValue("body", out var body))
        {
            Debug.WriteLine("⚠️ Not a Rust+ notification - missing channelId or body");
            return;
        }

        appDataDict.TryGetValue("title", out var title);
        appDataDict.TryGetValue("projectId", out var projectId);
        appDataDict.TryGetValue("experienceId", out var experienceId);
        appDataDict.TryGetValue("scopeKey", out var scopeKey);
        appDataDict.TryGetValue("message", out var message);

        var bodyData = JsonSerializer.Deserialize<Body>(body, _parsingOptions);

        var fcmMessage = new FcmMessage
        {
            PersistentId = dataMessage.PersistentId ?? string.Empty,
            From = long.Parse(dataMessage.From, CultureInfo.InvariantCulture),
            SentAt = DateTimeOffset.FromUnixTimeMilliseconds(dataMessage.Sent ?? 0).UtcDateTime,
            Data = new MessageData
            {
                ChannelId = channelId,
                ProjectId = Guid.Parse(projectId ?? Guid.Empty.ToString()),
                Body = bodyData!,
                Title = title ?? string.Empty,
                Message = message ?? string.Empty,
                ExperienceId = experienceId ?? string.Empty,
                ScopeKey = scopeKey ?? string.Empty,
            }
        };

        if (dataMessage.PersistentId is not null)
        {
            persistentIds?.Add(dataMessage.PersistentId);
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
