using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;

using Google.Protobuf;

using RustPlusContracts;

using static System.GC;
// ReSharper disable MemberCanBeProtected.Global
// ReSharper disable MemberCanBePrivate.Global

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
    public abstract class RustPlusSocket(string server, int port, ulong playerId, int playerToken, bool useFacepunchProxy = false) : IDisposable
    {
        private ClientWebSocket? _webSocket;

        private uint _seq;

        private readonly ConcurrentQueue<AppRequest> _sendQueue = new();
        private readonly ConcurrentQueue<TaskCompletionSource<AppMessage>> _responseQueue = new();

        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private CancellationToken _cancellationToken => _cancellationTokenSource.Token;

        public event EventHandler? Connecting;
        public event EventHandler? Connected;

        public event EventHandler? SendingRequest;
        public event EventHandler<AppRequest>? RequestSent;

        public event EventHandler<AppMessage>? MessageReceived;
        public event EventHandler<AppMessage>? NotificationReceived;
        public event EventHandler<AppMessage>? ResponseReceived;

        public event EventHandler? Disconnecting;
        public event EventHandler? Disconnected;

        public event EventHandler<Exception>? ErrorOccurred;

        /// <summary>
        /// Connects to the Rust+ server asynchronously.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task ConnectAsync()
        {
            _webSocket = new ClientWebSocket();
            _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

            var uri = useFacepunchProxy
                ? new Uri($"wss://companion-rust.facepunch.com/game/{server}/{port}")
                : new Uri($"ws://{server}:{port}");

            Connecting?.Invoke(this, EventArgs.Empty);

            try
            {
                await _webSocket.ConnectAsync(uri, CancellationToken.None);

                _ = Task.Run(ReceiveAsync, CancellationToken.None);
                _ = Task.Run(ProcessSendQueueAsync, CancellationToken.None);

                Connected?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception occured on ConnectAsync: {ex}");
                ErrorOccurred?.Invoke(this, ex);
            }
        }

        /// <summary>
        /// Sends a request to the Rust+ server asynchronously.
        /// </summary>
        /// <param name="request">The request to send.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task<AppMessage> SendRequestAsync(AppRequest request)
        {
            var tcs = new TaskCompletionSource<AppMessage>();
            var seq = Interlocked.Increment(ref _seq);

            request.Seq = seq;
            request.PlayerId = playerId;
            request.PlayerToken = playerToken;

            SendingRequest?.Invoke(this, EventArgs.Empty);

            _sendQueue.Enqueue(request);
            _responseQueue.Enqueue(tcs);

            RequestSent?.Invoke(this, request);

            return await tcs.Task;
        }

        /// <summary>
        /// Processes the send queue asynchronously.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task ProcessSendQueueAsync()
        {
            while (IsConnected() && !_cancellationToken.IsCancellationRequested)
            {
                if (_sendQueue.TryDequeue(out var request))
                {
                    var buffer = request.ToByteArray();
                    await _webSocket!.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Binary, true, CancellationToken.None);
                }
                await Task.Delay(100, CancellationToken.None);
            }
        }

        /// <summary>
        /// Receives data from the Rust+ server asynchronously.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task ReceiveAsync()
        {
            const int bufferSize = 1024;
            var buffer = new byte[bufferSize];

            Debug.WriteLine("Receiving data from the Rust+ server...");

            while (IsConnected() && !_cancellationToken.IsCancellationRequested)
            {
                Debug.WriteLine("Waiting for data...");
                try
                {
                    var receiveBuffer = new List<byte>();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await _webSocket!.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        receiveBuffer.AddRange(buffer.Take(result.Count));
                    } while (!result.EndOfMessage);

                    var messageData = receiveBuffer.ToArray();
                    var message = AppMessage.Parser.ParseFrom(messageData);

                    Debug.WriteLine($"Received message:\n{message}");
                    MessageReceived?.Invoke(this, message);

                    if (message.Broadcast is not null)
                    {
                        Debug.WriteLine($"Received notification:\n{message}");
                        NotificationReceived?.Invoke(this, message);
                        ParseNotification(message.Broadcast);
                    }
                    else
                    {
                        Debug.WriteLine($"Received response:\n{message}");
                        ResponseReceived?.Invoke(this, message);
                    }

                    _ = Task.Run(() =>
                    {
                        if (_responseQueue.TryDequeue(out var tcs))
                            tcs.SetResult(message);
                    });
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("Operation canceled.");
                    break;
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
            }
            Debug.WriteLine("Receive loop exited.");
        }

        /// <summary>
        /// Disconnects from the Rust+ server asynchronously.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task DisconnectAsync(bool forceClose = false)
        {
            if (!IsConnected()) return;

            Disconnecting?.Invoke(this, EventArgs.Empty);

            // Not sure about that
            while (!_responseQueue.IsEmpty && !forceClose)
            {
                await Task.Delay(50, CancellationToken.None);
            }

            // For some reason I have to wait
            await Task.Delay(1000, CancellationToken.None).ContinueWith(async (t) =>
            {
                await _webSocket!.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
            }, CancellationToken.None);

            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Parses the notification received from the Rust+ server.
        /// </summary>
        /// <param name="broadcast">The AppBroadcast received from the server.</param>
        protected virtual void ParseNotification(AppBroadcast? broadcast) { }

        /// <summary>
        /// Disposes the Rust+ API client and disconnects from the Rust+ server.
        /// </summary>
        public void Dispose() => SuppressFinalize(this);

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
        protected static bool IsError(AppMessage response)
        {
            if (response.Response is null && response.Broadcast is not null) return false;
            if (response.Response!.Error is not null) return true;
            return false;
        }
    }
}
