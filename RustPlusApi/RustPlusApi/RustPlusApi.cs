using System.Net.WebSockets;

using Google.Protobuf;

using Rustplus;

namespace RustPlusApi
{
    public class RustPlusApi(string server, int port, ulong playerId, int playerToken, bool useFacepunchProxy = false) : IDisposable
    {
        private ClientWebSocket? _webSocket;
        private uint _seq;
        private readonly Dictionary<int, Func<AppMessage, bool>> _seqCallbacks = new();

        public event EventHandler? Connecting;
        public event EventHandler? Connected;
        public event EventHandler<AppMessage>? MessageReceived;
        public event EventHandler<AppRequest>? RequestSent;
        public event EventHandler? Disconnected;
        public event EventHandler<Exception>? ErrorOccurred;

        public async Task ConnectAsync()
        {
            _webSocket = new ClientWebSocket();
            _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            var address = useFacepunchProxy
                ? new Uri($"wss://companion-rust.facepunch.com/game/{server}/{port}")
                : new Uri($"ws://{server}:{port}");

            Connecting?.Invoke(this, EventArgs.Empty);

            try
            {
                await _webSocket.ConnectAsync(address, CancellationToken.None);
                Connected?.Invoke(this, EventArgs.Empty);
                await ReceiveMessagesAsync();
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
                Disconnect();
            }
        }

        private async Task ReceiveMessagesAsync()
        {
            const int bufferSize = 1024;
            while (_webSocket!.State == WebSocketState.Open)
            {
                var buffer = new byte[bufferSize];
                var receiveBuffer = new List<byte>();
                WebSocketReceiveResult result;

                do
                {
                    result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    receiveBuffer.AddRange(buffer.Take(result.Count));
                } while (!result.EndOfMessage);

                var messageData = receiveBuffer.ToArray();
                var message = AppMessage.Parser.ParseFrom(messageData);
                MessageReceived?.Invoke(this, message);
                HandleResponse(message);
            }
        }

        private void HandleResponse(AppMessage message)
        {
            if (message.Response != null && message.Response.Seq != 0 && _seqCallbacks.ContainsKey((int)message.Response.Seq))
            {
                var callback = _seqCallbacks[(int)message.Response.Seq];
                var result = callback.Invoke(message);
                _seqCallbacks.Remove((int)message.Response.Seq);
                if (result) return;
            }
            MessageReceived?.Invoke(this, message);
        }

        private async Task SendRequestAsync(AppRequest request, Func<AppMessage, bool>? callback = null)
        {
            var seq = ++_seq;
            if (callback != null) _seqCallbacks[(int)seq] = callback;

            request.Seq = seq;
            request.PlayerId = playerId;
            request.PlayerToken = playerToken;

            var requestData = request.ToByteArray();
            var buffer = new ArraySegment<byte>(requestData);
            await _webSocket!.SendAsync(buffer, WebSocketMessageType.Binary, true, CancellationToken.None);
            RequestSent?.Invoke(this, request);
        }

        private void Disconnect()
        {
            if (_webSocket is not { State: WebSocketState.Open }) return;

            _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed by client.", CancellationToken.None).Wait();
            _webSocket.Dispose();

            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
            => Disconnect();

        public bool IsConnected()
            => _webSocket is { State: WebSocketState.Open };

        private async Task SetEntityValueAsync(int entityId, bool value, Func<AppMessage, bool>? callback = null)
        {
            var request = new AppRequest
            {
                EntityId = (uint)entityId,
                SetEntityValue = new AppSetEntityValue
                {
                    Value = value
                }
            };
            await SendRequestAsync(request, callback);
        }

        public async Task TurnSmartSwitchOnAsync(int entityId, Func<AppMessage, bool>? callback = null)
        {
            await SetEntityValueAsync(entityId, true, callback);
        }

        public async Task TurnSmartSwitchOffAsync(int entityId, Func<AppMessage, bool>? callback = null)
        {
            await SetEntityValueAsync(entityId, false, callback);
        }

        public async Task StrobeAsync(int entityId, int timeoutMilliseconds = 100, bool value = true)
        {
            await SetEntityValueAsync(entityId, value);
            await Task.Delay(timeoutMilliseconds);
            await StrobeAsync(entityId, timeoutMilliseconds, !value);
        }

        public async Task SendTeamMessageAsync(string message, Func<AppMessage, bool>? callback = null)
        {
            var request = new AppRequest
            {
                SendTeamMessage = new AppSendMessage
                {
                    Message = message
                }
            };
            await SendRequestAsync(request, callback);
        }

        public async Task GetEntityInfoAsync(int entityId, Func<AppMessage, bool>? callback = null)
        {
            var request = new AppRequest
            {
                EntityId = (uint)entityId,
                GetEntityInfo = new AppEmpty()
            };
            await SendRequestAsync(request, callback);
        }

        public async Task GetMapAsync(Func<AppMessage, bool>? callback = null)
        {
            var request = new AppRequest
            {
                GetMap = new AppEmpty()
            };
            await SendRequestAsync(request, callback);
        }

        public async Task GetTimeAsync(Func<AppMessage, bool>? callback = null)
        {
            var request = new AppRequest
            {
                GetTime = new AppEmpty()
            };
            await SendRequestAsync(request, callback);
        }

        public async Task GetInfoAsync(Func<AppMessage, bool>? callback = null)
        {
            var request = new AppRequest
            {
                GetInfo = new AppEmpty()
            };
            await SendRequestAsync(request, callback);
        }

        public async Task GetTeamInfoAsync(Func<AppMessage, bool>? callback = null)
        {
            var request = new AppRequest
            {
                GetTeamInfo = new AppEmpty()
            };
            await SendRequestAsync(request, callback);
        }
    }
}
