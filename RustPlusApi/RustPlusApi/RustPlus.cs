using System.Net.WebSockets;

using Google.Protobuf;

using RustPlusContracts;

using static System.GC;

namespace RustPlusApi
{
    /// <summary>
    /// A Rust+ API client made in C#.
    /// </summary>
    /// <param name="server">The IP address of the Rust+ server.</param>
    /// <param name="port">The port dedicated for the Rust+ companion app (not the one used to connect in-game).</param>
    /// <param name="playerId">Your Steam ID.</param>
    /// <param name="playerToken">Your player token acquired with FCM.</param>
    /// <param name="useFacepunchProxy">Specifies whether to use the Facepunch proxy.</param>
    public class RustPlus(string server, int port, ulong playerId, int playerToken, bool useFacepunchProxy = false) : IDisposable
    {
        private ClientWebSocket? _webSocket;
        private uint _seq;
        private readonly Dictionary<int, Func<AppMessage, bool>> _seqCallbacks = [];

        public event EventHandler? Connecting;
        public event EventHandler? Connected;
        public event EventHandler<AppMessage>? MessageReceived;
        public event EventHandler<AppRequest>? RequestSent;
        public event EventHandler? Disconnected;
        public event EventHandler<Exception>? ErrorOccurred;

        /// <summary>
        /// Connects to the Rust+ server asynchronously.
        /// </summary>
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
                Dispose();
            }
        }

        /// <summary>
        /// Receives messages from the Rust+ server asynchronously.
        /// </summary>
        private async Task ReceiveMessagesAsync()
        {
            const int bufferSize = 1024;
            var buffer = new byte[bufferSize];

            try
            {
                while (_webSocket!.State == WebSocketState.Open)
                {
                    var receiveBuffer = new List<byte>();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        receiveBuffer.AddRange(buffer.Take(result.Count));
                    } while (!result.EndOfMessage);

                    var messageData = receiveBuffer.ToArray();
                    var message = AppMessage.Parser.ParseFrom(messageData);
                    HandleResponse(message);
                }
            }
            catch (WebSocketException ex)
            {
                ErrorOccurred?.Invoke(this, ex);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
            }
        }

        /// <summary>
        /// Handles the response received from the Rust+ server.
        /// </summary>
        /// <param name="message">The AppMessage received from the server.</param>
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

        /// <summary>
        /// Sends a request to the Rust+ server asynchronously.
        /// </summary>
        /// <param name="request">The request to send.</param>
        /// <param name="callback">An optional callback function to handle the response.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
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

        /// <summary>
        /// Disposes the Rust+ API client and disconnects from the Rust+ server.
        /// </summary>
        public void Dispose()
        {
            if (_webSocket is not { State: WebSocketState.Open }) return;

            _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed by client.", CancellationToken.None).Wait();
            _webSocket.Dispose();

            Disconnected?.Invoke(this, EventArgs.Empty);

            SuppressFinalize(this);
        }

        /// <summary>
        /// Checks if the client is connected to the Rust+ server.
        /// </summary>
        /// <returns>True if the client is connected; otherwise, false.</returns>
        public bool IsConnected() => _webSocket is { State: WebSocketState.Open };

        /// <summary>
        /// Retrieves information about an entity from the Rust+ server asynchronously.
        /// </summary>
        /// <param name="entityId">The ID of the entity to retrieve information for.</param>
        /// <param name="callback">An optional callback function to handle the response.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task GetEntityInfoAsync(int entityId, Func<AppMessage, bool>? callback = null)
        {
            var request = new AppRequest
            {
                EntityId = (uint)entityId,
                GetEntityInfo = new AppEmpty()
            };
            await SendRequestAsync(request, callback);
        }

        /// <summary>
        /// Retrieves general information from the Rust+ server asynchronously.
        /// </summary>
        /// <param name="callback">An optional callback function to handle the response.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task GetInfoAsync(Func<AppMessage, bool>? callback = null)
        {
            var request = new AppRequest
            {
                GetInfo = new AppEmpty()
            };
            await SendRequestAsync(request, callback);
        }

        /// <summary>
        /// Retrieves the map from the Rust+ server asynchronously.
        /// </summary>
        /// <param name="callback">An optional callback function to handle the response.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task GetMapAsync(Func<AppMessage, bool>? callback = null)
        {
            var request = new AppRequest
            {
                GetMap = new AppEmpty()
            };
            await SendRequestAsync(request, callback);
        }

        /// <summary>
        /// Retrieves the map markers from the Rust+ server asynchronously.
        /// </summary>
        /// <param name="callback">An optional callback function to handle the response.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task GetMapMarkersAsync(Func<AppMessage, bool>? callback = null)
        {
            var request = new AppRequest
            {
                GetMapMarkers = new AppEmpty()
            };
            await SendRequestAsync(request, callback);
        }

        /// <summary>
        /// Retrieves team information from the Rust+ server asynchronously.
        /// </summary>
        /// <param name="callback">An optional callback function to handle the response.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task GetTeamInfoAsync(Func<AppMessage, bool>? callback = null)
        {
            var request = new AppRequest
            {
                GetTeamInfo = new AppEmpty()
            };
            await SendRequestAsync(request, callback);
        }

        /// <summary>
        /// Retrieves the current time from the Rust+ server asynchronously.
        /// </summary>
        /// <param name="callback">An optional callback function to handle the response.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task GetTimeAsync(Func<AppMessage, bool>? callback = null)
        {
            var request = new AppRequest
            {
                GetTime = new AppEmpty()
            };
            await SendRequestAsync(request, callback);
        }

        /// <summary>
        /// Sends a team message to the Rust+ server asynchronously.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="callback">An optional callback function to handle the response.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
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

        /// <summary>
        /// Sets the value of an entity asynchronously.
        /// </summary>
        /// <param name="entityId">The ID of the entity to set the value for.</param>
        /// <param name="value">The value to set.</param>
        /// <param name="callback">An optional callback function to handle the response.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task SetEntityValueAsync(int entityId, bool value, Func<AppMessage, bool>? callback = null)
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

        /// <summary>
        /// Toggles the value of an entity repeatedly with a specified timeout.
        /// </summary>
        /// <param name="entityId">The ID of the entity to toggle the value for.</param>
        /// <param name="timeoutMilliseconds">The timeout in milliseconds between toggling the value.</param>
        /// <param name="value">The initial value to set.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task StrobeAsync(int entityId, int timeoutMilliseconds = 1000, bool value = true)
        {
            await SetEntityValueAsync(entityId, value);
            await Task.Delay(timeoutMilliseconds);
            await SetEntityValueAsync(entityId, !value);
        }
    }
}
