using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using Google.Protobuf;
using RustPlusContracts;
using static System.GC;
// ReSharper disable MemberCanBeProtected.Global
// ReSharper disable MemberCanBePrivate.Global

namespace RustPlusApi;

/// <summary>
/// A Rust+ API client made in C#.
/// </summary>
/// <param name="server">The IP address of the Rust+ server.</param>
/// <param name="port">The port dedicated for the Rust+ companion app (not the one used to connect in-game).</param>
/// <param name="playerId">Your Steam ID.</param>
/// <param name="playerToken">Your player token acquired with FCM.</param>
/// <param name="useFacepunchProxy">Specifies whether to use the Facepunch proxy.</param>
public abstract class RustPlusSocket(
    string server,
    int port,
    ulong playerId,
    int playerToken,
    bool useFacepunchProxy = false)
    : IDisposable
{
    /// <summary>
    /// Occurs when the client is about to connect to the Rust+ server.
    /// </summary>
    /// <seealso cref="ConnectAsync"/>
    public event EventHandler? Connecting;

    /// <summary>
    /// Occurs when the client has successfully connected to the Rust+ server.
    /// </summary>
    /// <seealso cref="ConnectAsync"/>
    public event EventHandler? Connected;

    /// <summary>
    /// Occurs when a request is about to be sent to the Rust+ server.
    /// </summary>
    /// <seealso cref="SendRequestAsync(AppRequest)"/>
    public event EventHandler? SendingRequest;

    /// <summary>
    /// Occurs after a request has been sent to the Rust+ server.
    /// </summary>
    /// <seealso cref="SendRequestAsync(AppRequest)"/>
    public event EventHandler<AppRequest>? RequestSent;

    /// <summary>
    /// Occurs when a message is received from the Rust+ server.
    /// </summary>
    /// <seealso cref="AppMessage"/>
    public event EventHandler<AppMessage>? MessageReceived;

    /// <summary>
    /// Occurs when a notification (broadcast) is received from the Rust+ server.
    /// </summary>
    /// <seealso cref="AppMessage"/>
    public event EventHandler<AppMessage>? NotificationReceived;

    /// <summary>
    /// Occurs when a response is received from the Rust+ server.
    /// </summary>
    /// <seealso cref="AppMessage"/>
    public event EventHandler<AppMessage>? ResponseReceived;

    /// <summary>
    /// Occurs when the client is about to disconnect from the Rust+ server.
    /// </summary>
    /// <seealso cref="DisconnectAsync(bool)"/>
    public event EventHandler? Disconnecting;

    /// <summary>
    /// Occurs when the client has disconnected from the Rust+ server.
    /// </summary>
    /// <seealso cref="DisconnectAsync(bool)"/>
    public event EventHandler? Disconnected;

    /// <summary>
    /// Occurs when an error is encountered during communication with the Rust+ server.
    /// </summary>
    /// <seealso cref="Exception"/>
    public event EventHandler<Exception>? ErrorOccurred;
    
    private ClientWebSocket? _webSocket;

    private uint _seq;

    private readonly ConcurrentQueue<AppRequest> _sendQueue = new();
    private readonly ConcurrentQueue<TaskCompletionSource<AppMessage>> _responseQueue = new();

    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private CancellationToken CancellationToken => _cancellationTokenSource.Token;

    private int _playerToken = playerToken;
    private ulong _playerId = playerId;

    /// <summary>
    /// Asynchronously connects to the Rust+ server using a WebSocket.
    /// Raises <see cref="Connecting"/> before attempting to connect and <see cref="Connected"/> upon successful connection.
    /// Starts background tasks for receiving and sending messages.
    /// If an exception occurs, <see cref="ErrorOccurred"/> is raised.
    /// </summary>
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
    /// Sets the player ID and player token for the Rust+ API client.
    /// </summary>
    /// <param name="newPlayerId">The new Steam ID to use.</param>
    /// <param name="newPlayerToken">The new player token acquired with FCM.</param>
    public void SetPlayer(ulong newPlayerId, int newPlayerToken)
    {
        _playerId = newPlayerId;
        _playerToken = newPlayerToken;
    }

    /// <summary>
    /// Asynchronously sends a request to the Rust+ server.
    /// The request is enqueued and the method returns a task that completes when a response is received.
    /// Raises <see cref="SendingRequest"/> before enqueuing and <see cref="RequestSent"/> after enqueuing.
    /// </summary>
    /// <param name="request">The <see cref="AppRequest"/> to send.</param>
    /// <returns>A task that represents the asynchronous operation and contains the <see cref="AppMessage"/> response.</returns>
    public async Task<AppMessage> SendRequestAsync(AppRequest request)
    {
        var tcs = new TaskCompletionSource<AppMessage>();
        var seq = Interlocked.Increment(ref _seq);

        request.Seq = seq;
        request.PlayerId = _playerId;
        request.PlayerToken = _playerToken;

        SendingRequest?.Invoke(this, EventArgs.Empty);

        _sendQueue.Enqueue(request);
        _responseQueue.Enqueue(tcs);

        RequestSent?.Invoke(this, request);

        return await tcs.Task;
    }

    /// <summary>
    /// Asynchronously disconnects from the Rust+ server, waiting for pending responses unless <paramref name="forceClose"/> is true.
    /// Raises <c>Disconnecting</c> before disconnecting and <c>Disconnected</c> after.
    /// </summary>
    public async Task DisconnectAsync(bool forceClose = false)
    {
        if (!IsConnected()) return;

        Disconnecting?.Invoke(this, EventArgs.Empty);

        // Not sure about that
        while (!_responseQueue.IsEmpty && !forceClose)
        {
            await Task.Delay(50, CancellationToken.None);
        }

        // For some reason, I have to wait
        await Task.Delay(1000, CancellationToken.None).ContinueWith(async _ =>
        {
            await _webSocket!.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
        }, CancellationToken.None);

        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Disposes the Rust+ API client and disconnects from the Rust+ server.
    /// </summary>
    public void Dispose() => SuppressFinalize(this);

    /// <summary>
    /// Determines whether the client is currently connected to the Rust+ socket.
    /// </summary>
    /// <returns>True if the WebSocket is open; otherwise, false.</returns>
    public bool IsConnected() => _webSocket is { State: WebSocketState.Open };

    /// <summary>
    /// Parses and handles a broadcast notification received from the Rust+ server.
    /// Intended to be overridden in derived classes to implement custom notification handling logic.
    /// </summary>
    protected virtual void ParseNotification(AppBroadcast? broadcast) { }

    /// <summary>
    /// Determines whether the specified <see cref="AppMessage"/> response contains an error.
    /// Returns false if the message is a broadcast without a response.
    /// </summary>
    /// <param name="response">The <see cref="AppMessage"/> to check.</param>
    /// <returns>True if the response contains an error; otherwise, false.</returns>
    protected static bool IsError(AppMessage response)
    {
        if (response.Response is null && response.Broadcast is not null) return false;
        return response.Response!.Error is not null;
    }
    
    /// <summary>
    /// Continuously processes the sent queue by dequeuing requests and sending them to the Rust+ server
    /// via the WebSocket connection as binary messages.
    /// Waits for 100 milliseconds between each iteration.
    /// </summary>
    private async Task ProcessSendQueueAsync()
    {
        while (IsConnected() && !CancellationToken.IsCancellationRequested)
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
    /// Continuously receives messages from the Rust+ server via the WebSocket connection.
    /// Processes incoming data, parses messages, and raises events for received messages, notifications, and responses.
    /// Handles cancellation and exceptions, and signals errors through the <c>ErrorOccurred</c> event.
    /// </summary>
    /// <exception cref="WebSocketException">
    /// Thrown if a WebSocket error occurs during receiving.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the WebSocket is in an invalid state.
    /// </exception>
    private async Task ReceiveAsync()
    {
        const int bufferSize = 1024;
        var buffer = new byte[bufferSize];

        Debug.WriteLine("Receiving data from the Rust+ server...");

        while (IsConnected() && !CancellationToken.IsCancellationRequested)
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
                    // TODO: Check for a refactor to incorporate the entity type
                    // message.Response.EntityInfo.Type.
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
                }, CancellationToken);
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
}
