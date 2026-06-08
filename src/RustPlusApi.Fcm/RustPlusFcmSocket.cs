using McsProto;
using ProtoBuf;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;
using RustPlusApi.Fcm.Interfaces;
using System.Diagnostics;
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
    : IRustPlusFcmSocket, IDisposable
{
    private const string Host = "mtalk.google.com";
    private const int Port = 5228;

    private const int KMcsVersion = 41;

    private TcpClient? _tcpClient;
    private SslStream? _sslStream;

    /// <summary>The transport stream used for reading and writing MCS frames.
    /// In production this is set to the authenticated <see cref="SslStream"/> immediately after
    /// TLS handshake; tests supply an in-memory stream via <see cref="RunReceiveLoopOverStream"/>.</summary>
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
    /// </summary>
    public async Task ConnectAsync()
    {
        Connecting?.Invoke(this, EventArgs.Empty);

        _tcpClient = new TcpClient();
#if NET10_0_OR_GREATER
        await _tcpClient.ConnectAsync(Host, Port, CancellationToken).ConfigureAwait(false);
#else
        // netstandard2.0 lacks the CancellationToken overload.
        await _tcpClient.ConnectAsync(Host, Port).ConfigureAwait(false);
#endif

        _sslStream = new SslStream(_tcpClient.GetStream(), false);
        await _sslStream.AuthenticateAsClientAsync(Host).ConfigureAwait(false);
        _transport = _sslStream;

        try
        {
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

            SendPacket(loginRequest);

            Connected?.Invoke(this, EventArgs.Empty);

            _ = Task.Run(ReceiveMessages, CancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Exception occured on ConnectAsync: {ex}");
            ErrorOccurred?.Invoke(this, ex);
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
    }

    /// <summary>Test seam: runs the MCS receive/dispatch loop against an arbitrary stream,
    /// bypassing the live TLS connect. Internal — visible only to RustPlusApi.Tests.</summary>
    /// <param name="stream">The stream to read MCS frames from and write responses to.</param>
    internal void RunReceiveLoopOverStream(Stream stream)
    {
        _transport = stream;
        ReceiveMessages();
    }

    /// <summary>
    /// Continuously receives and processes messages from the FCM server over the SSL stream.
    /// Validates the protocol version and login response, then enters a loop to handle incoming messages.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the protocol version is unsupported, or if the initial response is not a <see cref="LoginResponse"/>.
    /// </exception>
    private void ReceiveMessages()
    {
        // Read the header
        var header = Read(2);
        int version = header[0];
        int tag = header[1];

        if (version is < KMcsVersion and not 38)
        {
            throw new InvalidOperationException($"Protocol version {version} unsupported");
        }

        var size = ReadVarInt32();
        var payload = Read(size);
        var type = BuildProtobufFromTag((McsProtoTag)tag);

        if (type != typeof(LoginResponse))
        {
            throw new InvalidOperationException($"Got wrong login response. Expected {nameof(LoginResponse)}, got {type.Name}");
        }

        OnGotMessageBytes(payload, type);

        while (!CancellationToken.IsCancellationRequested)
        {
            // Read the tag and size
            tag = _transport!.ReadByte();
            size = ReadVarInt32();
            payload = Read(size);
            type = BuildProtobufFromTag((McsProtoTag)tag);

            OnGotMessageBytes(payload, type);
        }
    }

    /// <summary>
    /// Deserializes a protobuf message from the given byte array and dispatches it to the message handler.
    /// </summary>
    /// <param name="data">The message bytes.</param>
    /// <param name="type">The type of the protobuf message.</param>
    private void OnGotMessageBytes(byte[] data, Type type)
    {
        try
        {
            var messageTag = GetTagFromProtobufType(type);

            if (data.Length == 0)
            {
                OnMessage(new MessageEventArgs { Tag = messageTag, Object = Activator.CreateInstance(type) });
                return;
            }

            var buffer = data.Take(data.Length).ToArray();

            using var stream = new MemoryStream(buffer);
            var message = Serializer.NonGeneric.Deserialize(type, stream);

            OnMessage(new MessageEventArgs { Tag = messageTag, Object = message });
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
    private byte[] Read(int size)
    {
        var buffer = new byte[size];
        var bytesRead = 0;
        while (bytesRead < size)
        {
            bytesRead += _transport!.Read(buffer, bytesRead, size - bytesRead);
        }
        return buffer;
    }

    /// <summary>
    /// Reads a variable-length 32-bit integer from the SSL stream.
    /// </summary>
    /// <returns>The decoded 32-bit integer.</returns>
    private int ReadVarInt32()
    {
        var result = 0;
        var shift = 0;
        while (true)
        {
            var b = (byte)_transport!.ReadByte();
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
    /// Serializes and sends a protobuf packet over the SSL stream.
    /// </summary>
    /// <param name="packet">The packet object to serialize and send.</param>
    private void SendPacket(object packet)
    {
        var tagEnum = GetTagFromProtobufType(packet.GetType());
        var header = new byte[] { KMcsVersion, (byte)(int)tagEnum };

        using var ms = new MemoryStream();
        Serializer.Serialize(ms, packet);

        var payload = ms.ToArray();
        byte[] frame = [.. header, .. EncodeVarInt32(payload.Length), .. payload];
        _transport!.Write(frame, 0, frame.Length);
    }

    /// <summary>
    /// Handles an incoming FCM heartbeat ping by sending a corresponding heartbeat acknowledgment.
    /// </summary>
    /// <param name="ping">The <see cref="HeartbeatPing"/> message received from the server.</param>
    private void HandlePing(HeartbeatPing? ping)
    {
        if (ping is null)
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

        SendPacket(pingResponse);
    }

    /// <summary>
    /// Handles incoming protocol messages by dispatching them based on their tag.
    /// Unrecognized tags are ignored so the receive loop keeps running.
    /// </summary>
    /// <param name="e">The <see cref="MessageEventArgs"/> containing the message tag and object.</param>
    private void OnMessage(MessageEventArgs e)
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
                HandlePing(e.Object as HeartbeatPing);
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
            PersistantId = dataMessage.PersistentId ?? string.Empty,
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
