using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Numerics;

using McsProto;

using ProtoBuf;

using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Gcm;
using RustPlusApi.Fcm.Utils;

using static RustPlusApi.Fcm.Data.Constants;
using static System.GC;

namespace RustPlusApi.Fcm
{
    public class FcmListener(Credentials credentials, ICollection<string> persistentIds) : IDisposable
    {
        private const string Host = "mtalk.google.com";
        private const int Port = 5228;

        private TcpClient? _tcpClient;
        private SslStream? _sslStream;

        public event EventHandler? Connecting;
        public event EventHandler? Connected;
        public event EventHandler<string>? NotificationReceived;
        public event EventHandler? Disconnected;
        public event EventHandler<Exception>? ErrorOccurred;

        public async Task ConnectAsync()
        {
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(Host, Port);

            _sslStream = new SslStream(_tcpClient.GetStream(), false);
            await _sslStream.AuthenticateAsClientAsync(Host);

            Connecting?.Invoke(this, EventArgs.Empty);

            try
            {
                Connected?.Invoke(this, EventArgs.Empty);

                var checkIn = await GcmTools.CheckInAsync(credentials.Gcm.AndroidId, credentials.Gcm.SecurityToken);

                var buffer = LoginBuffer();
                await _sslStream.WriteAsync(buffer);

                await ReceiveMessagesAsync();
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
                Dispose();
            }
        }

        public void Dispose()
        {
            _sslStream?.Dispose();
            _tcpClient?.Dispose();

            Disconnected?.Invoke(this, EventArgs.Empty);

            SuppressFinalize(this);
        }

        private async Task ReceiveMessagesAsync()
        {
            var buffer = new byte[4096];

            var parser = new Parser();
            parser.MessageReceived += (_, e) => OnMessage(e);

            while (true)
            {
                var bytesRead = await _sslStream!.ReadAsync(buffer);
                if (bytesRead == 0) return;

                var data = new byte[bytesRead];
                Array.Copy(buffer, data, bytesRead);

                parser.OnData(data);
            }
        }

        private byte[] LoginBuffer()
        {
            var loginRequest = new LoginRequest
            {
                AdaptiveHeartbeat = false,
                AuthService = LoginRequest.Types.AuthService.AndroidId,
                AuthToken = credentials.Gcm.SecurityToken.ToString(),
                Id = "chrome-63.0.3234.0",
                Domain = "mcs.android.com",
                DeviceId = $"android-{BigInteger.Parse(credentials.Gcm.AndroidId.ToString()):X}",
                NetworkType = 1,
                Resource = credentials.Gcm.AndroidId.ToString(),
                User = credentials.Gcm.AndroidId.ToString(),
                UseRmq2 = true,
                Setting = { new Setting { Name = "new_vc", Value = "1" } },
                ClientEvent = { },
                ReceivedPersistentId = { persistentIds },
            };
            using var stream = new MemoryStream();
            Serializer.Serialize(stream, loginRequest);
            var buffer = stream.ToArray();

            var header = new byte[] { KMcsVersion, (byte)McsProtoTag.KLoginRequestTag };
            return [.. header, .. buffer];
        }

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
                default:
                    throw new ArgumentOutOfRangeException($"Unrecognized tag: {e.Tag}");
            }
        }

        private void OnDataMessage(DataMessageStanza? dataMessage)
        {
            if (dataMessage?.PersistentId != null && persistentIds!.Contains(dataMessage?.PersistentId!)) return;

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
                    persistentIds?.Add(dataMessage!.PersistentId);
                    return;
                }
            }
            persistentIds?.Add(dataMessage!.PersistentId);
            NotificationReceived?.Invoke(this, message);
        }
    }
}
