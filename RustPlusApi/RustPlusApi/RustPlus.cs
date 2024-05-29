using System.Diagnostics;
using System.Net.WebSockets;

using Google.Protobuf;

using RustPlusApi.Data.Events;
using RustPlusApi.Extensions;

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
        private readonly Dictionary<int, TaskCompletionSource<AppMessage>> _seqCallbacks = [];

        public event EventHandler? Connecting;
        public event EventHandler? Connected;
        public event EventHandler<AppMessage>? MessageReceived;
        public event EventHandler<AppRequest>? RequestSent;
        public event EventHandler? Disconnected;
        public event EventHandler<Exception>? ErrorOccurred;

        public event EventHandler<SmartSwitchEventArg>? OnSmartSwitchTriggered; // The Alarm behave exactly like the SmartSwitch so if you get the status of the alarm, this will be triggered
        public event EventHandler<StorageMonitorEventArg>? OnStorageMonitorTriggered;

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
        /// <returns>A task representing the asynchronous operation.</returns>
        protected async Task ReceiveMessagesAsync()
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
                Debug.WriteLine($"Disconnected from the Rust+ socket due to a WebSocketException: {ex}");
                ErrorOccurred?.Invoke(this, ex);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Disconnected from the Rust+ socket due to an Exception: {ex}");
                ErrorOccurred?.Invoke(this, ex);
            }
            finally
            {
                Dispose();
            }
        }

        /// <summary>
        /// Handles the response received from the Rust+ server.
        /// </summary>
        /// <param name="message">The AppMessage received from the server.</param>
        protected void HandleResponse(AppMessage message)
        {
            if (message.Response != null
                && message.Response.Seq != 0
                && _seqCallbacks.ContainsKey((int)message.Response.Seq))
            {
                var tcs = _seqCallbacks[(int)message.Response.Seq];
                tcs.SetResult(message);
                _seqCallbacks.Remove((int)message.Response.Seq);
                return;
            }
            MessageReceived?.Invoke(this, message);
            ParseNotification(message.Broadcast);
        }

        /// <summary>
        /// Sends a request to the Rust+ server asynchronously.
        /// </summary>
        /// <param name="request">The request to send.</param>
        /// <return>A task representing the asynchronous operation.</returns>
        public async Task<AppMessage> SendRequestAsync(AppRequest request)
        {
            var seq = ++_seq;
            var tcs = new TaskCompletionSource<AppMessage>();
            _seqCallbacks[(int)seq] = tcs;

            request.Seq = seq;
            request.PlayerId = playerId;
            request.PlayerToken = playerToken;

            var requestData = request.ToByteArray();
            var buffer = new ArraySegment<byte>(requestData);
            await _webSocket!.SendAsync(buffer, WebSocketMessageType.Binary, true, CancellationToken.None);
            RequestSent?.Invoke(this, request);

            return await tcs.Task;
        }

        /// <summary>
        /// Parses the notification received from the Rust+ server.
        /// </summary>
        /// <param name="broadcast">The AppBroadcast received from the server.</param>
        protected void ParseNotification(AppBroadcast? broadcast)
        {
            if (broadcast is null) return;

            if (broadcast.EntityChanged is not null)
            {
                // It is physically impossible to differentiate between a SmartSwitch and an Alarm
                // This is a limitation of the Rust+ API
                if (broadcast.EntityChanged.Payload.Capacity is 0)
                    OnSmartSwitchTriggered?.Invoke(this, broadcast.EntityChanged.ToSmartSwitchEvent());
                else
                    OnStorageMonitorTriggered?.Invoke(this, broadcast.EntityChanged.ToStorageMonitorEvent());
            }
            else Debug.WriteLine($"Unknown broadcast:\n{broadcast}");
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
        /// Checks if the client is connected to the Rust+ socket.
        /// </summary>
        /// <returns>True if the client is connected; otherwise, false.</returns>
        public bool IsConnected() => _webSocket is { State: WebSocketState.Open };

        /// <summary>
        /// Checks if the given response is an error.
        /// </summary>
        /// <param name="response">The AppMessage response to check.</param>
        /// <returns>True if the response is an error; otherwise, false.</returns>
        private static bool IsError(AppMessage response) => response.Response.Error is not null;

        /// <summary>
        /// Processes a request to the Rust+ server asynchronously.
        /// </summary>
        /// <param name="request">The request to process.</param>
        /// <param name="useRawObject">Specifies whether to use the raw object or convert it to a custom object.</param>
        /// <param name="successSelector">A function to select the success response.</param>
        /// <returns>The processed response.</returns>
        private async Task<object> ProcessRequestAsync(AppRequest request, bool useRawObject, Func<AppMessage, object> successSelector)
        {
            var response = await SendRequestAsync(request);

            if (IsError(response))
                return useRawObject
                    ? response
                    : response.Response.Error;

            return useRawObject
                ? response
                : successSelector(response);
        }

        /// <summary>
        /// Retrieves information about an entity from the Rust+ server asynchronously.
        /// </summary>
        /// <param name="entityId">The ID of the entity to retrieve information for.</param>
        /// <param name="useRawObject">Specifies whether to use the raw object or convert it to a custom entity info object.</param>
        /// <returns>
        /// The entity information.
        /// If useRawObject is true, it returns an instance of the AppMessage class.
        /// If useRawObject is false, it returns a custom entity info object such as SmartSwitchInfo, AlarmInfo, or StorageMonitorInfo.
        /// </returns>
        public async Task<object> GetEntityInfoAsync(uint entityId, bool useRawObject = false)
        {
            var request = new AppRequest
            {
                EntityId = entityId,
                GetEntityInfo = new AppEmpty()
            };
            return await ProcessRequestAsync(request, useRawObject, r => r.Response.EntityInfo.ToEntityInfo());
        }

        /// <summary>
        /// Retrieves information about the Rust+ server asynchronously.
        /// </summary>
        /// <param name="useRawObject">Specifies whether to use the raw object or convert it to a custom info object.</param>
        /// <returns>
        /// The server information.
        /// If useRawObject is true, it returns an instance of the AppMessage class.
        /// If useRawObject is false, it returns an ServerInfo.
        /// </returns>
        public async Task<object> GetInfoAsync(bool useRawObject = false)
        {
            var request = new AppRequest
            {
                GetInfo = new AppEmpty()
            };
            return await ProcessRequestAsync(request, useRawObject, r => r.Response.Info.ToServerInfo());
        }

        /// <summary>
        /// Retrieves the map from the Rust+ server asynchronously.
        /// </summary>
        /// <param name="useRawObject">Specifies whether to use the raw object or convert it to a custom map object.</param>
        /// <returns>
        /// The map.
        /// If useRawObject is true, it returns an instance of the AppMessage class.
        /// If useRawObject is false, it returns a MapInfo.
        /// </returns>
        public async Task<object> GetMapAsync(bool useRawObject = false)
        {
            var request = new AppRequest
            {
                GetMap = new AppEmpty()
            };
            return await ProcessRequestAsync(request, useRawObject, r => r.Response.Map.ToServerMap());
        }
        /*
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
        /// Retrieves the team chat from the Rust+ server asynchronously.
        /// </summary>
        /// <param name="callback">An optional callback function to handle the response.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task GetTeamChatAsync(Func<AppMessage, bool>? callback = null)
        {
            var request = new AppRequest
            {
                GetTeamChat = new AppEmpty(),
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
        /// Retrieves the team chat from the Rust+ server asynchronously.
        /// </summary>
        /// <param name="steamId">The steam ID of the new group owner</param>
        /// <param name="callback">An optional callback function to handle the response.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task PromoteToLeaderAsync(ulong steamId, Func<AppMessage, bool>? callback = null)
        {
            var request = new AppRequest
            {
                PromoteToLeader = new AppPromoteToLeader
                {
                    SteamId = steamId
                }
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

        public async Task GetClanChatAsync(Func<AppMessage, bool>? callback = null)
        {
            var request = new AppRequest
            {
                GetClanChat = new AppEmpty()
            };
            await SendRequestAsync(request, callback);
        }

        public async Task GetClanInfoAsync(Func<AppMessage, bool>? callback = null)
        {
            var request = new AppRequest
            {
                GetClanInfo = new AppEmpty()
            };
            await SendRequestAsync(request, callback);
        }*/
    }
}