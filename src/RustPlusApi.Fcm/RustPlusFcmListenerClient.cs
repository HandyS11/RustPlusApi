using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using McsProto;
using ProtoBuf;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;
using static System.GC;
using static RustPlusApi.Fcm.Data.Tags;
using static RustPlusApi.Fcm.Utils.Utils;

namespace RustPlusApi.Fcm;

/// <summary>
/// Represents a RustPlus FCM listener client for handling FCM connections and notifications.
/// </summary>
/// <param name="credentials">The <see cref="Credentials"/> used for authentication.</param>
/// <param name="persistentIds">The collection of persistent IDs as <see cref="ICollection{T}"/> of <see cref="string"/>.</param>
public class RustPlusFcmListenerClient(Credentials credentials, ICollection<string>? persistentIds = null) : IDisposable
{
    private const string Host = "mtalk.google.com";
    private const int Port = 5228;

    private const int KMcsVersion = 41;

    private TcpClient? _tcpClient;
    private SslStream? _sslStream;

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

    public async Task ConnectAsync()
    {
        Connecting?.Invoke(this, EventArgs.Empty);

        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(Host, Port, CancellationToken);

        _sslStream = new SslStream(_tcpClient.GetStream(), false);
        await _sslStream.AuthenticateAsClientAsync(Host);

        try
        {
            var loginRequest = new LoginRequest
            {
                AdaptiveHeartbeat = false,
                auth_service = LoginRequest.AuthService.AndroidId,
                AuthToken = credentials.Gcm.SecurityToken.ToString(),
                Id = "chrome-63.0.3234.0",
                Domain = "mcs.android.com",
                DeviceId = $"android-{BigInteger.Parse(credentials.Gcm.AndroidId.ToString()):X}",
                NetworkType = 1,
                Resource = credentials.Gcm.AndroidId.ToString(),
                User = credentials.Gcm.AndroidId.ToString(),
                UseRmq2 = true,
                Settings = { new Setting { Name = "new_vc", Value = "1" } },
                ClientEvents = { new ClientEvent() },
            };

            if (persistentIds != null) loginRequest.ReceivedPersistentIds.AddRange(persistentIds);

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
    /// Releases resources used by the <see cref="RustPlusFcmListenerClient"/>.
    /// </summary>
    public void Dispose() => SuppressFinalize(this);

    /// <summary>
    /// Continuously receives and processes messages from the FCM server over the SSL stream.
    /// Validates the protocol version and login response, then enters a loop to handle incoming messages.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the protocol version is unsupported.
    /// </exception>
    /// <exception cref="Exception">
    /// Thrown if the initial response is not a <see cref="LoginResponse"/>.
    /// </exception>
    private void ReceiveMessages()
    {
        // Read the header
        var header = Read(2);
        int version = header[0];
        int tag = header[1];

        if (version is < KMcsVersion and not 38)
            throw new InvalidOperationException($"Protocol version {version} unsupported");

        var size = ReadVarInt32();
        var payload = Read(size);
        var type = BuildProtobufFromTag((McsProtoTag)tag);

        if (type != typeof(LoginResponse))
#pragma warning disable CA2201
            throw new Exception($"Got wrong login response. Expected {nameof(LoginResponse)}, got {type.Name}");
#pragma warning restore CA2201

        OnGotMessageBytes(payload, type);

        while (!CancellationToken.IsCancellationRequested)
        {
            // Read the tag and size
            tag = _sslStream!.ReadByte();
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
            bytesRead += _sslStream!.Read(buffer, bytesRead, size - bytesRead);
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
            var b = (byte)_sslStream!.ReadByte();
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
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
        _sslStream!.Write([.. header, .. EncodeVarInt32(payload.Length), .. payload]);
    }

    /// <summary>
    /// Handles an incoming FCM heartbeat ping by sending a corresponding heartbeat acknowledgment.
    /// </summary>
    /// <param name="ping">The <see cref="HeartbeatPing"/> message received from the server.</param>
    private void HandlePing(HeartbeatPing? ping)
    {
        if (ping == null) return;

        Debug.WriteLine($"Responding to ping: Stream ID: {ping.StreamId}, Last: {ping.LastStreamIdReceived}, Status: {ping.Status}");
        var pingResponse = new HeartbeatAck
        {
            StreamId = ping.StreamId + 1,
            LastStreamIdReceived = ping.StreamId,
            Status = ping.Status
        };

        SendPacket(pingResponse);
    }

    /// <summary>
    /// Handles incoming protocol messages by dispatching them based on their tag.
    /// </summary>
    /// <param name="e">The <see cref="MessageEventArgs"/> containing the message tag and object.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if the message tag is unrecognized.
    /// </exception>
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
                throw new ArgumentOutOfRangeException($"Unrecognized tag: {e.Tag}");
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
            persistentIds != null &&
            persistentIds.Contains(dataMessage.PersistentId))
        {
            return;
        }

        if (dataMessage?.AppDatas is not { Count: > 0 })
        {
            Console.WriteLine("⚠️ No AppData found in message");
            return;
        }

        var appDataDict = dataMessage.AppDatas.ToDictionary(x => x.Key, x => x.Value);

        if (!appDataDict.TryGetValue("channelId", out var channelId) ||
            !appDataDict.TryGetValue("body", out var body))
        {
            Console.WriteLine("⚠️ Not a Rust+ notification - missing channelId or body");
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
            PersistantId = dataMessage.PersistentId,
            From = long.Parse(dataMessage.From),
            SentAt = DateTimeOffset.FromUnixTimeMilliseconds(dataMessage.Sent).UtcDateTime,
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

        persistentIds?.Add(dataMessage.PersistentId);

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
