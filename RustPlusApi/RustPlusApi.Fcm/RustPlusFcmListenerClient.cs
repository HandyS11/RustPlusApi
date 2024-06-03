using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Numerics;

using McsProto;

using ProtoBuf;

using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;
using RustPlusApi.Fcm.Utils;

using static RustPlusApi.Fcm.Data.Tags;
using static RustPlusApi.Fcm.Utils.Utils;
using static System.GC;

namespace RustPlusApi.Fcm
{
    public class RustPlusFcmListenerClient(Credentials credentials, ICollection<string>? persistentIds = null) : IDisposable
    {
        private const string Host = "mtalk.google.com";
        private const int Port = 5228;

        private const int MinResetIntervalSecs = 5 * 60;
        private const int MaxSilentIntervalSecs = 60 * 60;

        private const int KMcsVersion = 41;

        private TcpClient? _tcpClient;
        private SslStream? _sslStream;

        private DateTime _lastReset;
        private DateTime _timeLastMessageReceived;
        private Timer? _checkinTimer;

        public event EventHandler? Connecting;
        public event EventHandler? Connected;

        public event EventHandler<string>? NotificationReceived;

        public event EventHandler? Disconnecting;
        public event EventHandler? Disconnected;

        public event EventHandler<Exception>? ErrorOccurred;

        public async Task ConnectAsync()
        {
            Connecting?.Invoke(this, EventArgs.Empty);

            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(Host, Port);

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

                _lastReset = DateTime.Now;
                _timeLastMessageReceived = DateTime.Now;

                Connected?.Invoke(this, EventArgs.Empty);

                StatusCheck();
                ReceiveMessages();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception occured on ConnectAsync: {ex}");
                ErrorOccurred?.Invoke(this, ex);
            }
        }

        /// <summary>
        /// Disconnects the FCM listener client asynchronously.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public void Disconnect()
        {
            Disconnecting?.Invoke(this, EventArgs.Empty);

            _sslStream?.Close();
            _tcpClient?.Close();

            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Disposes the FCM listener client.
        /// </summary>
        public void Dispose() => SuppressFinalize(this);

        private void ReceiveMessages()
        {
            var parser = new RawMessageParser();
            parser.MessageReceived += (_, e) => OnMessage(e);

            byte[] header = Read(2);
            int version = header[0];
            int tag = header[1];

            if (version < KMcsVersion && version != 38)
                throw new InvalidOperationException($"Protocol version {version} unsupported");

            var size = ReadVarint32();
            Debug.WriteLine($"Got message size: {size} bytes");

            var payload = Read(size);
            Debug.WriteLine($"Successfully read: {payload.Length} bytes");

            Type type = BuildProtobufFromTag((McsProtoTag)tag);
            Debug.WriteLine($"RECEIVED PROTO OF TYPE {type.Name}");

            if (type != typeof(LoginResponse))
                throw new Exception($"Got wrong login response. Expected {typeof(LoginResponse).Name}, got {type.Name}");

            parser.OnGotLoginResponse();
            parser.OnData(payload, type);

            // Start receival of the rest of messages
            Debug.WriteLine("Starting receiver loop.");
            while (true)
            {
                tag = _sslStream!.ReadByte();
                size = ReadVarint32();
                payload = Read(size);
                type = BuildProtobufFromTag((McsProtoTag)tag);
                Debug.WriteLine($"RECEIVED PROTO OF TYPE {type.Name}");

                parser.OnData(payload, type);
            }
        }

        private byte[] Read(int size)
        {
            byte[] buffer = new byte[size];
            int bytesRead = 0;
            while (bytesRead < size)
            {
                bytesRead += _sslStream!.Read(buffer, bytesRead, size - bytesRead);
            }
            return buffer;
        }

        private int ReadVarint32()
        {
            int result = 0;
            int shift = 0;
            while (true)
            {
                byte b = (byte)_sslStream!.ReadByte();
                result |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
            }
            return result;
        }

        private void SendPacket(object packet)
        {
            var tagEnum = GetTagFromProtobufType(packet.GetType());
            var header = new byte[] { KMcsVersion, (byte)(int)tagEnum };

            using var ms = new MemoryStream();
            Serializer.Serialize(ms, packet);

            byte[] payload = ms.ToArray();
            _sslStream!.Write([.. header, .. EncodeVarint32(payload.Length), .. payload]);
        }

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

        private void Reset(bool noWait = false)
        {
            if (!noWait)
            {
                var timeSinceLastReset = DateTime.Now - _lastReset;

                if (timeSinceLastReset < TimeSpan.FromSeconds(MinResetIntervalSecs))
                {
                    Debug.WriteLine($"{timeSinceLastReset.TotalSeconds}s since last reset attempt.");

                    var waitTime = TimeSpan.FromSeconds(MinResetIntervalSecs) - timeSinceLastReset;

                    Debug.WriteLine($"Waiting {waitTime.TotalSeconds}seconds");
                    Thread.Sleep(waitTime);
                }
            }
            _lastReset = DateTime.Now;

            Debug.WriteLine("Resetting listener.");
            Disconnect();
            ConnectAsync().GetAwaiter().GetResult();
        }

        private void StatusCheck(object? state = null)
        {
            TimeSpan timeSinceLastMessage = DateTime.UtcNow - _timeLastMessageReceived;
            if (timeSinceLastMessage > TimeSpan.FromSeconds(MaxSilentIntervalSecs))
            {
                Debug.WriteLine($"No communications received in {timeSinceLastMessage.TotalSeconds}s. Resetting connection.");
                Reset(true);
            }
            else
            {
                int expectedTimeout = 1 + MaxSilentIntervalSecs - (int)timeSinceLastMessage.TotalSeconds;
                _checkinTimer = new Timer(StatusCheck, null, expectedTimeout * 1000, Timeout.Infinite);
            }
        }

        private void OnMessage(MessageEventArgs e)
        {
            _timeLastMessageReceived = DateTime.Now;

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
                    Reset(true);
                    break;
                case McsProtoTag.KIqStanzaTag:
                    break; // I'm not sure about what this message does, and it arrives partially empty, so I will just leave it like this for now
                default:
                    throw new ArgumentOutOfRangeException($"Unrecognized tag: {e.Tag}");
            }
        }

        private void OnDataMessage(DataMessageStanza? dataMessage)
        {
            if (dataMessage?.PersistentId != null
                && persistentIds != null
                && persistentIds!.Contains(dataMessage?.PersistentId!))
                return;

            var message = string.Empty;
            try
            {
                message = DecryptionUtility.Decrypt(dataMessage!, credentials.Keys);
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("Unsupported state or unable to authenticate data") ||
                    ex.Message.Contains("crypto-key is missing") ||
                    ex.Message.Contains("salt is missing"))
                {
                    Debug.WriteLine($"Message dropped as it could not be decrypted: {ex.Message}");
                    return;
                }
            }
            finally
            {
                persistentIds?.Add(dataMessage!.PersistentId);
            }

            ParseNotification(message);
            NotificationReceived?.Invoke(this, message);
        }

        protected virtual void ParseNotification(string message) { }
    }
}
