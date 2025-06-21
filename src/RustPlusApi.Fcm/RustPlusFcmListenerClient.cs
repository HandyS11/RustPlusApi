using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Numerics;
using McsProto;
using Newtonsoft.Json;
using ProtoBuf;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;
using static System.GC;
using static RustPlusApi.Fcm.Data.Tags;
using static RustPlusApi.Fcm.Utils.Utils;

namespace RustPlusApi.Fcm;

/// <summary>
/// Represents a RustPlus FCM listener.
/// </summary>
/// <param name="credentials">The credentials used for authentication.</param>
/// <param name="persistentIds">The collection of persistent IDs.</param>
public class RustPlusFcmListenerClient(Credentials credentials, ICollection<string>? persistentIds = null) : IDisposable
{
    private const string Host = "mtalk.google.com";
    private const int Port = 5228;

    private const int KMcsVersion = 41;

    private TcpClient? _tcpClient;
    private SslStream? _sslStream;

    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private CancellationToken CancellationToken => _cancellationTokenSource.Token;

    public event EventHandler? Connecting;
    public event EventHandler? Connected;

    public event EventHandler<string>? NotificationReceived;

    public event EventHandler? Disconnecting;
    public event EventHandler? Disconnected;

    public event EventHandler? SocketClosed;
    public event EventHandler<Exception>? ErrorOccurred;

    public async Task ConnectAsync()
    {
        Connecting?.Invoke(this, EventArgs.Empty);

        // Skip check-in for now - JavaScript already did it during registration
        Console.WriteLine("Using pre-registered FCM credentials...");

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
                Settings = { new Setting() { Name = "new_vc", Value = "1" } },
                ClientEvents = { new ClientEvent() },
                ReceivedPersistentIds = { },
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

    private async Task CheckInAsync()
    {
        using var httpClient = new HttpClient();

        // Build proper check-in protobuf (based on JavaScript implementation)
        var checkinData = BuildProperCheckinRequest();

        var response = await httpClient.PostAsync(
            "https://android.clients.google.com/checkin",
            new ByteArrayContent(checkinData)
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-protobuf") }
            }
        );

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Google FCM check-in failed: {response.StatusCode} - {errorContent}");
        }

        Console.WriteLine($"✅ Google check-in successful: {response.StatusCode}");
    }

    private byte[] BuildProperCheckinRequest()
    {
        // Based on JavaScript push-receiver checkin.proto structure
        var data = new List<byte>();

        // Required fields for Android check-in (mimicking JavaScript implementation)

        // Field 2: androidId (int64)
        AppendField(data, 2, 0, credentials.Gcm.AndroidId);

        // Field 3: securityToken (int64) 
        AppendField(data, 3, 0, credentials.Gcm.SecurityToken);

        // Field 4: version (int32) - Chrome version
        AppendField(data, 4, 0, 3);

        // Field 9: chrome_build (message)
        var chromeBuild = new List<byte>();
        AppendStringField(chromeBuild, 1, "chrome"); // platform
        AppendStringField(chromeBuild, 2, "63.0.3234.0"); // chrome_version  
        AppendStringField(chromeBuild, 3, "x86_64"); // channel
        AppendField(data, 9, 2, chromeBuild.ToArray());

        // Field 12: macAddress (repeated string) - fake MAC addresses
        AppendStringField(data, 12, "02:00:00:00:00:00");

        // Field 13: meid (string) - fake MEID
        AppendStringField(data, 13, "A100000123456789");

        // Field 15: timeZone (string)
        AppendStringField(data, 15, "GMT");

        return data.ToArray();
    }

    private static void AppendField(List<byte> data, int fieldNumber, int wireType, ulong value)
    {
        // Field header
        data.Add((byte)((fieldNumber << 3) | wireType));

        // Encode value as varint
        while (value >= 0x80)
        {
            data.Add((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        data.Add((byte)(value & 0x7F));
    }

    private static void AppendField(List<byte> data, int fieldNumber, int wireType, byte[] value)
    {
        // Field header
        data.Add((byte)((fieldNumber << 3) | wireType));

        // Length-delimited: first encode length
        AppendVarint(data, (ulong)value.Length);

        // Then the data
        data.AddRange(value);
    }

    private static void AppendStringField(List<byte> data, int fieldNumber, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        AppendField(data, fieldNumber, 2, bytes); // Wire type 2 = length-delimited
    }

    private static void AppendVarint(List<byte> data, ulong value)
    {
        while (value >= 0x80)
        {
            data.Add((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        data.Add((byte)(value & 0x7F));
    }

    /// <summary>
    /// Disconnects the FCM listener client asynchronously.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public void Disconnect()
    {
        Disconnecting?.Invoke(this, EventArgs.Empty);

        _cancellationTokenSource.Cancel();

        _sslStream?.Close();
        _tcpClient?.Close();

        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Disposes the FCM listener client.
    /// </summary>
    public void Dispose() => SuppressFinalize(this);

    /// <summary>
    /// Receives messages from the FCM listener.
    /// </summary>
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
            throw new Exception($"Got wrong login response. Expected {nameof(LoginResponse)}, got {type.Name}");

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
    /// Handles the received message bytes.
    /// </summary>
    /// <param name="data">The message bytes.</param>
    /// <param name="type">The type of the message.</param>
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
            data = data.Skip(data.Length).ToArray();

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
    /// <returns>An array of bytes read from the stream.</returns>
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
    /// <returns>The 32-bit integer read from the stream.</returns>
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
    /// Sends a packet over the SSL stream.
    /// </summary>
    /// <param name="packet">The packet to send.</param>
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
    /// Handles a ping message by sending a ping response.
    /// </summary>
    /// <param name="ping">The ping message to handle.</param>
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
    /// Handles a message received event.
    /// </summary>
    /// <param name="e">The message event arguments.</param>
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
                break; // I'm not sure about what this message does, and it arrives partially empty, so I will just leave it like this for now
            default:
                throw new ArgumentOutOfRangeException($"Unrecognized tag: {e.Tag}");
        }
    }

    /// <summary>
    /// Handles a data message received event.
    /// </summary>
    /// <param name="dataMessage">The data message stanza.</param>
    private void OnDataMessage(DataMessageStanza? dataMessage)
    {
        Console.WriteLine($"🔔 FCM MESSAGE RECEIVED! PersistentId: {dataMessage?.PersistentId}");
        Console.WriteLine($"🔔 RawData Length: {dataMessage?.RawData?.Length ?? 0} bytes");
        // Debug: Let's see what fields are populated
        Console.WriteLine($"🔍 DEBUG - Message fields:");
        Console.WriteLine($"   Category: {dataMessage?.Category}");
        Console.WriteLine($"   From: {dataMessage?.From}");
        Console.WriteLine($"   To: {dataMessage?.To}");
        Console.WriteLine($"   Token: {dataMessage?.Token}");
        Console.WriteLine($"   AppDatas Count: {dataMessage?.AppDatas?.Count ?? 0}");

        if (dataMessage?.AppDatas != null && dataMessage.AppDatas.Count > 0)
        {
            Console.WriteLine($"🔍 AppData contents:");
            foreach (var item in dataMessage.AppDatas)
            {
                Console.WriteLine($"   Key: '{item.Key}' = Value: '{item.Value}'");
            }
        }
        if (dataMessage?.PersistentId != null
            && persistentIds != null
            && persistentIds!.Contains(dataMessage?.PersistentId!))
        {
            Console.WriteLine("🔔 Message already processed, skipping...");
            return;
        }

        // Extract notification data from AppData (Rust+ notifications come as AppData, not encrypted RawData)
        string? message = null;
        if (dataMessage?.AppDatas != null && dataMessage.AppDatas.Count > 0)
        {
            // Convert AppData to dictionary for easier access
            var appDataDict = dataMessage.AppDatas.ToDictionary(x => x.Key, x => x.Value);

            // Check if this is a Rust+ notification
            if (appDataDict.TryGetValue("channelId", out var channelId) &&
                appDataDict.TryGetValue("body", out var bodyJson))
            {
                Console.WriteLine($"🎯 Found Rust+ notification! Channel: {channelId}");

                // Build the expected FcmMessage format
                var fcmMessage = new
                {
                    channelId = channelId,
                    body = JsonConvert.DeserializeObject(bodyJson) // Parse the JSON body
                };

                message = JsonConvert.SerializeObject(fcmMessage);
                Console.WriteLine($"✅ Converted to FCM format: {message}");
            }
            else
            {
                Console.WriteLine("⚠️ Not a Rust+ notification - missing channelId or body"); return;
            }
        }
        else
        {
            Console.WriteLine("⚠️ No AppData found in message");
            return;
        }        // Add to persistent IDs to avoid reprocessing
        persistentIds?.Add(dataMessage!.PersistentId);

        Console.WriteLine($"🎯 ABOUT TO PARSE NOTIFICATION:");
        Console.WriteLine($"🎯 Message: {message}");
        Console.WriteLine($"🎯 Message Length: {message?.Length ?? 0}");

        if (!string.IsNullOrEmpty(message))
        {
            ParseNotification(message);
            NotificationReceived?.Invoke(this, message);
        }
        else
        {
            Console.WriteLine("⚠️ Message is null or empty, skipping notification parsing");
        }
    }

    /// <summary>
    /// Parses the notification message.
    /// </summary>
    /// <param name="message">The notification message to parse.</param>
    protected virtual void ParseNotification(string message) { }
}
