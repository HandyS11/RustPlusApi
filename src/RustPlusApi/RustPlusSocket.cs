using ProtoBuf;
using RustPlusApi.Interfaces;
using RustPlusContracts;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Threading.Channels;
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
    : IRustPlusSocket, IDisposable, IAsyncDisposable
{
    /// <summary>Bounds teardown waits so a wedged loop or dead peer cannot hang disposal.</summary>
    private static readonly TimeSpan TeardownTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Backoff applied after a non-fatal receive error so the loop cannot busy-spin on it.</summary>
    private static readonly TimeSpan ReceiveErrorBackoff = TimeSpan.FromMilliseconds(100);
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

    /// <summary>int (not uint) so Interlocked.Increment works on netstandard2.0, which lacks the uint overload.</summary>
    private int _seq;

    /// <summary>Outgoing requests are handed to the send loop via a channel — no polling, no per-send latency.</summary>
    private readonly Channel<AppRequest> _sendChannel = Channel.CreateUnbounded<AppRequest>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentQueue<TaskCompletionSource<AppMessage>> _responseQueue = new();

    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private CancellationToken CancellationToken => _cancellationTokenSource.Token;

    private Task? _receiveLoop;
    private Task? _sendLoop;

    /// <summary>Test seam: the tracked receive loop, so tests can assert it actually completed on teardown.</summary>
    internal Task? ReceiveLoopForTests => _receiveLoop;

    /// <summary>Test seam: the tracked send loop, so tests can assert it actually completed on teardown.</summary>
    internal Task? SendLoopForTests => _sendLoop;

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
            await _webSocket.ConnectAsync(uri, CancellationToken).ConfigureAwait(false);

            _receiveLoop = Task.Run(ReceiveAsync, CancellationToken);
            _sendLoop = Task.Run(ProcessSendQueueAsync, CancellationToken);

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

        request.Seq = (uint)Interlocked.Increment(ref _seq);
        request.PlayerId = _playerId;
        request.PlayerToken = _playerToken;

        SendingRequest?.Invoke(this, EventArgs.Empty);

        _sendChannel.Writer.TryWrite(request);
        _responseQueue.Enqueue(tcs);

        RequestSent?.Invoke(this, request);

        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously disconnects from the Rust+ server, waiting for pending responses unless <paramref name="forceClose"/> is true.
    /// Raises <c>Disconnecting</c> before disconnecting and <c>Disconnected</c> after.
    /// </summary>
    /// <param name="forceClose">When <see langword="true"/>, skips draining the pending-response queue.</param>
    public async Task DisconnectAsync(bool forceClose = false)
    {
        if (!IsConnected())
        {
            return;
        }

        Disconnecting?.Invoke(this, EventArgs.Empty);

        while (!_responseQueue.IsEmpty && !forceClose)
        {
            await Task.Delay(50, CancellationToken).ConfigureAwait(false);
        }

        // Give the server a moment to flush any in-flight responses before closing.
        await Task.Delay(1000, CancellationToken).ConfigureAwait(false);

        // Bound the close handshake: a dead peer that never acks must not hang teardown.
        using var closeTimeout = new CancellationTokenSource(TeardownTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, closeTimeout.Token);
        try
        {
            await _webSocket!.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Bounded close timed out (or the instance token was cancelled): drop the socket regardless.
        }
        catch (WebSocketException)
        {
            // Peer already gone; nothing to close gracefully.
        }

        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Disposes the Rust+ API client, cancelling background work and releasing the underlying WebSocket.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the resources used by the <see cref="RustPlusSocket"/>.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> to release both managed and unmanaged resources;
    /// <see langword="false"/> to release only unmanaged resources.
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        if (!_cancellationTokenSource.IsCancellationRequested)
        {
            _cancellationTokenSource.Cancel();
        }

        _sendChannel.Writer.TryComplete();
        _webSocket?.Dispose();
        _cancellationTokenSource.Dispose();
    }

    /// <summary>
    /// Asynchronously disposes the client: cancels background work, awaits the tracked receive/send
    /// loops (bounded by <see cref="TeardownTimeout"/>), then releases the WebSocket. Prefer this over
    /// <see cref="Dispose()"/> so teardown deterministically drains in-flight I/O instead of abandoning it.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        SuppressFinalize(this);
    }

    /// <summary>
    /// Cancels the instance token, awaits the tracked background loops (bounded), and releases resources.
    /// Override to extend async teardown in derived classes.
    /// </summary>
    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (!_cancellationTokenSource.IsCancellationRequested)
        {
#if NET10_0_OR_GREATER
            await _cancellationTokenSource.CancelAsync().ConfigureAwait(false);
#else
            _cancellationTokenSource.Cancel();
#endif
        }

        _sendChannel.Writer.TryComplete();

        await WaitForLoopsAsync().ConfigureAwait(false);

        _webSocket?.Dispose();
        _cancellationTokenSource.Dispose();
    }

    /// <summary>
    /// Awaits the tracked receive/send loops, bounded by <see cref="TeardownTimeout"/> so a wedged loop
    /// cannot hang disposal. The loops swallow their own cancellation, so a clean stop completes promptly.
    /// </summary>
    private async Task WaitForLoopsAsync()
    {
        var loops = new[] { _receiveLoop, _sendLoop }
            .Where(static t => t is not null)
            .Cast<Task>()
            .ToArray();
        if (loops.Length == 0)
        {
            return;
        }

        var all = Task.WhenAll(loops);
        var completed = await Task.WhenAny(all, Task.Delay(TeardownTimeout)).ConfigureAwait(false);
        if (completed != all)
        {
            return;
        }

        try
        {
            await all.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A loop faulting as the transport is torn down is expected during teardown; never let it
            // escape disposal. The loops swallow their own cancellation, so this is a genuine I/O fault.
            Debug.WriteLine($"Background loop faulted during teardown (expected): {ex}");
        }
    }

    /// <summary>
    /// Determines whether the client is currently connected to the Rust+ socket.
    /// </summary>
    /// <returns>True if the WebSocket is open; otherwise, false.</returns>
    public bool IsConnected() => _webSocket is { State: WebSocketState.Open };

    /// <summary>
    /// Parses and handles a broadcast notification received from the Rust+ server.
    /// Intended to be overridden in derived classes to implement custom notification handling logic.
    /// </summary>
    /// <param name="broadcast">The <see cref="AppBroadcast"/> received from the server.</param>
    protected virtual void ParseNotification(AppBroadcast? broadcast) { }

    /// <summary>
    /// Determines whether the specified <see cref="AppMessage"/> response contains an error.
    /// Returns false if the message is a broadcast without a response.
    /// </summary>
    /// <param name="response">The <see cref="AppMessage"/> to check.</param>
    /// <returns>True if the response contains an error; otherwise, false.</returns>
    protected static bool IsError(AppMessage response)
    {
        if (response.Broadcast is not null)
        {
            return false;
        }

        return response.Response.Error is not null;
    }

    /// <summary>
    /// Retrieves the error message from the specified <see cref="AppMessage"/> response.
    /// </summary>
    /// <param name="response">The <see cref="AppMessage"/> containing the response data.</param>
    /// <returns>
    /// A string representing the error message if the response contains an error;
    /// otherwise, returns "value-already-set".
    /// </returns>
    protected static string GetErrorMessage(AppMessage response)
    {
        return response.Response.Error is not null
            ? response.Response.Error.Error
            : "value-already-set";
    }

    /// <summary>
    /// Continuously drains the outgoing channel, serializing each request and sending it to the Rust+
    /// server as a binary WebSocket message. Awaits the channel rather than polling, so there is no
    /// per-send latency and no busy wakeups; exits when the token is cancelled or the channel completes.
    /// </summary>
    private async Task ProcessSendQueueAsync()
    {
        try
        {
            while (await _sendChannel.Reader.WaitToReadAsync(CancellationToken).ConfigureAwait(false))
            {
                while (_sendChannel.Reader.TryRead(out var request))
                {
#pragma warning disable RCS1261 // MemoryStream.DisposeAsync is a no-op; await using not available in netstandard2.0
                    using var ms = new MemoryStream();
#pragma warning restore RCS1261
                    Serializer.Serialize(ms, request);
                    var buffer = ms.ToArray();
                    await _webSocket!.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Binary, true, CancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Teardown cancelled the token mid-wait/send: exit the loop cleanly.
        }
        catch (WebSocketException ex)
        {
            // The socket broke under us (e.g. peer closed): surface it and stop draining.
            Debug.WriteLine($"Send loop stopped due to a WebSocketException: {ex}");
            ErrorOccurred?.Invoke(this, ex);
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
                    result = await _webSocket!.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken).ConfigureAwait(false);
                    receiveBuffer.AddRange(buffer.Take(result.Count));
                } while (!result.EndOfMessage);

                var messageData = receiveBuffer.ToArray();
#pragma warning disable RCS1261 // MemoryStream.DisposeAsync is a no-op; await using not available in netstandard2.0
                using var messageStream = new MemoryStream(messageData);
#pragma warning restore RCS1261
                var message = Serializer.Deserialize<AppMessage>(messageStream);

                Debug.WriteLine($"Received message:\n{message}");
                MessageReceived?.Invoke(this, message);

                if (message.Broadcast is not null)
                {
                    Debug.WriteLine($"Received notification:\n{message}");
                    NotificationReceived?.Invoke(this, message);
                    // Entity type from message.Response.EntityInfo.Type is not yet used.
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
                    {
                        tcs.SetResult(message);
                    }
                }, CancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Teardown cancelled the instance token: leave the receive loop without raising an error.
                break;
            }
            catch (WebSocketException ex)
            {
                // A WebSocket error means the connection is broken; retrying would immediately throw again.
                // Surface it and exit instead of busy-spinning on a dead socket.
                Debug.WriteLine($"Disconnected from the Rust+ socket due to a WebSocketException: {ex}");
                ErrorOccurred?.Invoke(this, ex);
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Disconnected from the Rust+ socket due to an Exception: {ex}");
                ErrorOccurred?.Invoke(this, ex);

                // Back off so a persistently failing receive cannot busy-spin the loop.
                try
                {
                    await Task.Delay(ReceiveErrorBackoff, CancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        Debug.WriteLine("Receive loop exited.");
    }
}
