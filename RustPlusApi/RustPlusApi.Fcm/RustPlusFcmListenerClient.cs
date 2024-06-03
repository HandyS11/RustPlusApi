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
        private CancellationToken _cancellationToken => _cancellationTokenSource.Token;

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

                Connected?.Invoke(this, EventArgs.Empty);

                _ = Task.Run(ReceiveMessages, _cancellationToken);
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

            if (version < KMcsVersion && version != 38)
                throw new InvalidOperationException($"Protocol version {version} unsupported");

            var size = ReadVarint32();
            var payload = Read(size);
            var type = BuildProtobufFromTag((McsProtoTag)tag);

            if (type != typeof(LoginResponse))
                throw new Exception($"Got wrong login response. Expected {typeof(LoginResponse).Name}, got {type.Name}");

            OnGotMessageBytes(payload, type);

            while (!_cancellationToken.IsCancellationRequested)
            {
                // Read the tag and size
                tag = _sslStream!.ReadByte();
                size = ReadVarint32();
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
            byte[] buffer = new byte[size];
            int bytesRead = 0;
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

            byte[] payload = ms.ToArray();
            _sslStream!.Write([.. header, .. EncodeVarint32(payload.Length), .. payload]);
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

        /// <summary>
        /// Parses the notification message.
        /// </summary>
        /// <param name="message">The notification message to parse.</param>
        protected virtual void ParseNotification(string message) { }
    }
}
