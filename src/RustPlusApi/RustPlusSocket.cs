using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ProtoBuf;
using RustPlusApi.Interfaces;
using RustPlusContracts;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading.Channels;
using static System.GC;

// ReSharper disable MemberCanBeProtected.Global
// ReSharper disable MemberCanBePrivate.Global

namespace RustPlusApi;

/// <summary>
/// A Rust+ API client made in C#.
/// </summary>
/// <param name="connection">The server endpoint and player credentials to connect as.</param>
/// <param name="options">Tuning options (timeouts, keep-alive, buffer size); defaults are used when <see langword="null"/>.</param>
/// <param name="loggerFactory">Routes the client's diagnostics into your logging stack; logging is
/// disabled (a no-op <c>NullLogger</c>) when <see langword="null"/>.</param>
public abstract class RustPlusSocket(
    RustPlusConnection connection,
    RustPlusSocketOptions? options = null,
    ILoggerFactory? loggerFactory = null)
    : IRustPlusSocket
{
    /// <summary>Tuning values for this instance — a private snapshot taken at construction, so later
    /// mutation of the caller's (possibly shared) options object cannot affect a live socket.</summary>
    private readonly RustPlusSocketOptions _options = options is null
        ? new RustPlusSocketOptions()
        : new()
        {
            RequestTimeout = options.RequestTimeout,
            KeepAliveInterval = options.KeepAliveInterval,
            TeardownTimeout = options.TeardownTimeout,
            ReceiveBufferSize = options.ReceiveBufferSize,
        };

    /// <summary>The client logger; <c>NullLogger</c> when no factory was supplied. Exposed to derived
    /// classes (e.g. <see cref="RustPlus"/>) so they log through the same categorised sink.</summary>
    private protected readonly ILogger Logger =
        (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger("RustPlusApi.RustPlusSocket");

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
    /// <seealso cref="SendRequestAsync"/>
    public event EventHandler? SendingRequest;

    /// <summary>
    /// Occurs after a request has been sent to the Rust+ server.
    /// </summary>
    /// <seealso cref="SendRequestAsync"/>
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
        new UnboundedChannelOptions
        {
            SingleReader = true
        });

    /// <summary>Requests answered by a seq-bearing <see cref="AppResponse"/>, keyed by <see cref="AppRequest.Seq"/>
    /// so each response resolves the request it actually answers; unsolicited broadcasts never touch this map.</summary>
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<AppMessage>> _pendingRequests = new();

    /// <summary>Requests answered by a broadcast (e.g. SetEntityValue → EntityChanged, SendTeamMessage →
    /// TeamMessage). Broadcasts carry no seq, so each waiter supplies a matcher describing the broadcast it
    /// expects (entity ID, own Steam ID, …); a broadcast that matches no waiter is a pure notification.
    /// Guarded by <see cref="_broadcastRepliesLock"/>.</summary>
    private readonly List<(Func<AppBroadcast, bool> Matches, TaskCompletionSource<AppMessage> Tcs)>
        _pendingBroadcastReplies = [];

    private readonly object _broadcastRepliesLock = new();

    /// <summary>Test seam: the number of in-flight requests awaiting a seq-bearing response.</summary>
    internal int PendingRequestCountForTests => _pendingRequests.Count;

    /// <summary>Test seam: enqueues a request directly onto the send channel, bypassing
    /// <see cref="SendRequestAsync"/>'s state checks, to exercise the send loop's fault path.</summary>
    /// <param name="request">The request to enqueue.</param>
    internal void EnqueueRequestForTests(AppRequest request) => _sendChannel.Writer.TryWrite(request);

    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private CancellationToken CancellationToken => _cancellationTokenSource.Token;

    /// <summary>Serializes connect/disconnect transitions so concurrent lifecycle calls cannot race
    /// on <see cref="_webSocket"/> (leaking a connection or briefly running two receive loops).</summary>
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    /// <summary>Test seam: the tracked receive loop, so tests can assert it actually completed on teardown.</summary>
    internal Task? ReceiveLoopForTests { get; private set; }

    /// <summary>Test seam: the tracked send loop, so tests can assert it actually completed on teardown.</summary>
    internal Task? SendLoopForTests { get; private set; }

    private readonly int _playerToken = connection.PlayerToken;

    /// <summary>The Steam ID requests are issued as.</summary>
    protected ulong PlayerId { get; } = connection.PlayerId;

    /// <summary>
    /// Asynchronously connects to the Rust+ server using a WebSocket.
    /// Raises <see cref="Connecting"/> before attempting to connect and <see cref="Connected"/> upon successful connection.
    /// Starts background tasks for receiving and sending messages.
    /// On failure, <see cref="ErrorOccurred"/> is raised and the exception is rethrown to the caller.
    /// An instance can reconnect: after <see cref="DisconnectAsync"/> (or a dropped connection), calling
    /// this again opens a fresh socket; the previous one is released, never leaked.
    /// </summary>
    /// <remarks>Connect/disconnect transitions are serialized internally and are not reentrant:
    /// an event handler (e.g. <see cref="Connected"/>) must not call back into
    /// <see cref="ConnectAsync"/> or <see cref="DisconnectAsync"/> and wait for it, or it will deadlock.</remarks>
    /// <param name="cancellationToken">A token to cancel the connection attempt.</param>
    /// <exception cref="ObjectDisposedException">Thrown when the client has been disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the client is already connected.</exception>
    /// <exception cref="WebSocketException">Thrown when the WebSocket connect fails (also raised via <see cref="ErrorOccurred"/>).</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is
    /// cancelled while waiting for a concurrent lifecycle transition or during the connect handshake.</exception>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
#if NET10_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_cancellationTokenSource.IsCancellationRequested, this);
#else
        // ObjectDisposedException.ThrowIf is unavailable on netstandard2.0.
        if (_cancellationTokenSource.IsCancellationRequested)
        {
            throw new ObjectDisposedException(GetType().FullName);
        }
#endif

        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsConnected)
            {
                throw new InvalidOperationException("Already connected. Call DisconnectAsync before reconnecting.");
            }

            // Reconnect support: release the previous (closed/dead) socket so it is never leaked, and
            // make sure its receive loop has exited so two loops can never read concurrently.
            if (_webSocket is not null)
            {
                _webSocket.Dispose();
                _webSocket = null;
                await WaitForReceiveLoopExitAsync().ConfigureAwait(false);
            }

            var webSocket = new ClientWebSocket();
            webSocket.Options.KeepAliveInterval = _options.KeepAliveInterval;

            var uri = connection.UseFacepunchProxy
                ? new Uri($"wss://companion-rust.facepunch.com/game/{connection.Server}/{connection.Port}")
                : new Uri($"ws://{connection.Server}:{connection.Port}");

            Connecting?.Invoke(this, EventArgs.Empty);

            try
            {
                // Either the caller's token or the instance token can cancel the connect handshake.
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, cancellationToken);
                await webSocket.ConnectAsync(uri, linked.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Surface the failure on the event *and* to the caller: awaiting ConnectAsync must never
                // "succeed" against a server that was never reached.
                webSocket.Dispose();
                Logger.LogConnectFailed(ex);
                ErrorOccurred?.Invoke(this, ex);
                throw;
            }

            _webSocket = webSocket;

            // The background loops outlive the connect call, so they track the instance token only.
            // The receive loop is bound to its own socket so a stale loop can never read a newer connection.
#pragma warning disable CA2025 // the loop owns this socket's read side by design; teardown/reconnect awaits the loop before disposing it
            ReceiveLoopForTests = Task.Run(() => ReceiveAsync(webSocket), CancellationToken);
#pragma warning restore CA2025

            // The send loop drains a single channel across reconnects; only start it if it is not running
            // (first connect, or it exited when a previous connection broke mid-send).
            if (SendLoopForTests is not { IsCompleted: false })
            {
                SendLoopForTests = Task.Run(ProcessSendQueueAsync, CancellationToken);
            }

            Connected?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            try
            {
                _lifecycleLock.Release();
            }
            catch (ObjectDisposedException)
            {
                // A concurrent dispose tore the instance down mid-transition. Disposal is
                // deliberately not serialized behind this lock — teardown must stay bounded —
                // so the semaphore can be gone by the time this transition unwinds.
            }
        }
    }

    /// <summary>
    /// Awaits the previous connection's receive loop before a reconnect, bounded by
    /// <see cref="RustPlusSocketOptions.TeardownTimeout"/>. Disposing the old socket unblocks its read, so a clean exit is
    /// prompt; a fault from that torn-down read is expected and swallowed.
    /// </summary>
    private async Task WaitForReceiveLoopExitAsync()
    {
        var loop = ReceiveLoopForTests;
        if (loop is null)
        {
            return;
        }

        var completed = await Task.WhenAny(loop, Task.Delay(_options.TeardownTimeout)).ConfigureAwait(false);
        if (completed != loop)
        {
            return;
        }

        try
        {
#pragma warning disable VSTHRD003 // we own this background task; awaiting it before reconnect cannot deadlock
            await loop.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        catch (Exception ex)
        {
            // The old loop faulting as its socket was torn down is expected; never block a reconnect on it.
            Logger.LogPreviousReceiveLoopFaulted(ex);
        }
    }

    /// <summary>
    /// Asynchronously sends a request to the Rust+ server and awaits the response correlated by sequence
    /// number. Raises <see cref="SendingRequest"/> before sending and <see cref="RequestSent"/> after.
    /// The wait honors <paramref name="cancellationToken"/>, the instance token, and a default timeout; on
    /// cancellation or timeout the pending entry is removed and the task faults with a clear exception.
    /// </summary>
    /// <param name="request">The <see cref="AppRequest"/> to send.</param>
    /// <param name="broadcastReplyMatcher">When non-null, the success reply is delivered as a broadcast
    /// (no seq); the first incoming broadcast this predicate matches resolves the request. Unrelated
    /// broadcasts (other players' messages, other entities) are left to the notification pipeline.</param>
    /// <param name="cancellationToken">A token to cancel waiting for the response.</param>
    /// <returns>A task that represents the asynchronous operation and contains the <see cref="AppMessage"/> response.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the client is not connected.</exception>
    /// <exception cref="TimeoutException">Thrown when no response arrives within the request timeout.</exception>
    public async Task<AppMessage> SendRequestAsync(AppRequest request,
        Func<AppBroadcast, bool>? broadcastReplyMatcher = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            // Queueing here would mean a generic 30s timeout now and a stale, out-of-context
            // request transmitted on the next reconnect — fail fast instead. The socket can still
            // die between this check and the actual send — that residual race is owned by the
            // send loop's fault handler, which fails the pending requests immediately.
            throw new InvalidOperationException("Not connected. Call ConnectAsync before sending requests.");
        }

        // RunContinuationsAsynchronously keeps the receive loop from running awaiters inline when it resolves the TCS.
        var tcs = new TaskCompletionSource<AppMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        var seq = (uint)Interlocked.Increment(ref _seq);
        request.Seq = seq;
        request.PlayerId = PlayerId;
        request.PlayerToken = _playerToken;

        // Always keyed by seq: even a broadcast-reply request gets a seq-bearing response on error.
        _pendingRequests[seq] = tcs;
        if (broadcastReplyMatcher is not null)
        {
            // …and additionally matched by predicate, because its success reply is a broadcast (no seq).
            lock (_broadcastRepliesLock)
            {
                _pendingBroadcastReplies.Add((broadcastReplyMatcher, tcs));
            }
        }

        SendingRequest?.Invoke(this, EventArgs.Empty);

        _sendChannel.Writer.TryWrite(request);

        RequestSent?.Invoke(this, request);

        using var timeoutCts = new CancellationTokenSource(_options.RequestTimeout);
        using var linked =
            CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, cancellationToken, timeoutCts.Token);

        try
        {
            // Cancel the wait if the caller cancels, the client is disposed, or the request times out.
            using (linked.Token.Register(static state => ((TaskCompletionSource<AppMessage>)state!).TrySetCanceled(),
                       tcs))
            {
                return await tcs.Task.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested
                                                 && !cancellationToken.IsCancellationRequested
                                                 && !CancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The Rust+ request (Seq {seq}) timed out after {_options.RequestTimeout.TotalSeconds:0.#}s.");
        }
        finally
        {
            _pendingRequests.TryRemove(seq, out _);
            if (broadcastReplyMatcher is not null)
            {
                lock (_broadcastRepliesLock)
                {
                    _pendingBroadcastReplies.RemoveAll(waiter => ReferenceEquals(waiter.Tcs, tcs));
                }
            }
        }
    }

    /// <summary>
    /// Asynchronously disconnects from the Rust+ server, waiting for pending responses unless <paramref name="forceClose"/> is true.
    /// Raises <c>Disconnecting</c> before disconnecting and <c>Disconnected</c> after.
    /// </summary>
    /// <remarks>Connect/disconnect transitions are serialized internally and are not reentrant:
    /// an event handler (e.g. <see cref="Disconnected"/>) must not call back into
    /// <see cref="ConnectAsync"/> or <see cref="DisconnectAsync"/> and wait for it, or it will deadlock.</remarks>
    /// <param name="forceClose">When <see langword="true"/>, skips draining in-flight requests.</param>
    public async Task DisconnectAsync(bool forceClose = false)
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsConnected)
            {
                return;
            }

            Disconnecting?.Invoke(this, EventArgs.Empty);

            if (!forceClose)
            {
                // Drain by awaiting the in-flight requests' completion (bounded), instead of polling a queue
                // and sleeping a fixed second before closing.
                await WaitForPendingRequestsAsync().ConfigureAwait(false);
            }

            // Bound the close handshake: a dead peer that never acks must not hang teardown.
            using var closeTimeout = new CancellationTokenSource(_options.TeardownTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, closeTimeout.Token);
            try
            {
                await _webSocket!.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, linked.Token)
                    .ConfigureAwait(false);
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
        finally
        {
            try
            {
                _lifecycleLock.Release();
            }
            catch (ObjectDisposedException)
            {
                // A concurrent dispose tore the instance down mid-transition. Disposal is
                // deliberately not serialized behind this lock — teardown must stay bounded —
                // so the semaphore can be gone by the time this transition unwinds.
            }
        }
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
        _lifecycleLock.Dispose();
    }

    /// <summary>
    /// Asynchronously disposes the client: cancels background work, awaits the tracked receive/send
    /// loops (bounded by <see cref="RustPlusSocketOptions.TeardownTimeout"/>), then releases the WebSocket. Prefer this over
    /// <see cref="Dispose()"/> so teardown deterministically drains in-flight I/O instead of abandoning it.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await DisposeCoreAsync().ConfigureAwait(false);
        SuppressFinalize(this);
    }

    /// <summary>
    /// Cancels the instance token, awaits the tracked background loops (bounded), and releases resources.
    /// Override to extend async teardown in derived classes.
    /// </summary>
    protected virtual async ValueTask DisposeCoreAsync()
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
        _lifecycleLock.Dispose();
    }

    /// <summary>
    /// Awaits the tracked receive/send loops, bounded by <see cref="RustPlusSocketOptions.TeardownTimeout"/> so a wedged loop
    /// cannot hang disposal. The loops swallow their own cancellation, so a clean stop completes promptly.
    /// </summary>
    private async Task WaitForLoopsAsync()
    {
        var loops = new[]
            {
                ReceiveLoopForTests, SendLoopForTests
            }
            .Where(static t => t is not null)
            .Cast<Task>()
            .ToArray();
        if (loops.Length == 0)
        {
            return;
        }

        var all = Task.WhenAll(loops);
        var completed = await Task.WhenAny(all, Task.Delay(_options.TeardownTimeout)).ConfigureAwait(false);
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
            Logger.LogTeardownLoopFaulted(ex);
        }
    }

    /// <summary>
    /// Awaits the settlement of all in-flight requests, bounded by <see cref="RustPlusSocketOptions.TeardownTimeout"/> so a server
    /// that never answers cannot hang a graceful disconnect. Faults are observed by each request's caller.
    /// </summary>
    private async Task WaitForPendingRequestsAsync()
    {
        var pending = _pendingRequests.Values.Select(static tcs => tcs.Task).ToArray();
        if (pending.Length == 0)
        {
            return;
        }

        var all = Task.WhenAll(pending);
        await Task.WhenAny(all, Task.Delay(_options.TeardownTimeout)).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a value indicating whether the client is currently connected to the Rust+ socket
    /// (the underlying WebSocket is open).
    /// </summary>
    public bool IsConnected => _webSocket is { State: WebSocketState.Open };

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
    /// The server's error string when the response carries an error; otherwise "unknown-error"
    /// (a response without an error payload should never reach this method, so don't invent a
    /// specific reason for it).
    /// </returns>
    protected static string GetErrorMessage(AppMessage response)
    {
        return response.Response.Error is not null
            ? response.Response.Error.Error
            : "unknown-error";
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
                    await _webSocket!.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Binary, true,
                        CancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Teardown cancelled the token mid-wait/send: exit the loop cleanly.
        }
        catch (WebSocketException ex)
        {
            // The socket broke under us (e.g. peer closed): surface it, stop draining, and fail the
            // in-flight requests now — none of them can ever be answered on this connection.
            Logger.LogSendLoopFaulted(ex);
            ErrorOccurred?.Invoke(this, ex);
            FailPendingRequests(ex);
        }
        catch (Exception ex)
        {
            // A concurrent reconnect can dispose/null _webSocket while this loop is draining
            // (NullReferenceException / ObjectDisposedException). Left uncaught, the loop dies
            // invisibly and every pending request waits out its full timeout — surface the fault
            // and fail them now instead. The receive loop's generic catch backs off and retries,
            // but exiting is correct here because the faulted socket cannot send anything again —
            // ConnectAsync restarts a completed send loop on the next connect.
            Logger.LogSendLoopFaulted(ex);
            ErrorOccurred?.Invoke(this, ex);
            FailPendingRequests(ex);
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
    /// <param name="webSocket">The connection this loop reads; bound per-loop so a stale loop from a
    /// previous connection can never read a newer socket during a reconnect.</param>
    private async Task ReceiveAsync(ClientWebSocket webSocket)
    {
        var buffer = new byte[_options.ReceiveBufferSize];

        Logger.LogReceivingData();

        while (webSocket.State == WebSocketState.Open && !CancellationToken.IsCancellationRequested)
        {
            Logger.LogWaitingForData();
            try
            {
                var message = await ReceiveMessageAsync(webSocket, buffer).ConfigureAwait(false);
                DispatchMessage(message);
            }
            catch (OperationCanceledException)
            {
                // Teardown cancelled the instance token: leave the receive loop without raising an error.
                break;
            }
            catch (WebSocketException ex)
            {
                // A WebSocket error means the connection is broken; retrying would immediately throw again.
                // Surface it and exit instead of busy-spinning on a dead socket. Fail the in-flight
                // requests now rather than leaving each to hit its full request timeout.
                Logger.LogReceiveWebSocketFault(ex);
                ErrorOccurred?.Invoke(this, ex);
                FailPendingRequests(ex);
                break;
            }
            catch (Exception ex)
            {
                Logger.LogReceiveFault(ex);
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

        Logger.LogReceiveLoopExited();

        // The loop also exits without an exception (server close frame, graceful disconnect). No
        // response can arrive any more, so fail whatever is still pending instead of letting each
        // request run out its full timeout. On disposal the waiters are already cancelled via the
        // instance token, so this is a no-op there.
        FailPendingRequests(new WebSocketException(
            WebSocketError.ConnectionClosedPrematurely,
            "The connection closed before a response arrived."));
    }

    /// <summary>
    /// Reads a single, possibly fragmented, message off <paramref name="webSocket"/> and deserializes it.
    /// </summary>
    /// <param name="webSocket">The connection to read from.</param>
    /// <param name="buffer">A reusable receive buffer, sized from the options.</param>
    /// <returns>The fully accumulated and deserialized <see cref="AppMessage"/>.</returns>
    private async Task<AppMessage> ReceiveMessageAsync(ClientWebSocket webSocket, byte[] buffer)
    {
        // Accumulate fragments straight into a stream: no per-read LINQ, no list growth,
        // and one buffer reused for the connection (map messages span megabytes).
#pragma warning disable RCS1261 // MemoryStream.DisposeAsync is a no-op; await using not available in netstandard2.0
        using var messageStream = new MemoryStream();
#pragma warning restore RCS1261
        WebSocketReceiveResult result;

        do
        {
            result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken)
                .ConfigureAwait(false);
#pragma warning disable CA1849, VSTHRD103, S6966 // MemoryStream.Write is in-memory and never blocks; WriteAsync would only add overhead
            messageStream.Write(buffer, 0, result.Count);
#pragma warning restore CA1849, VSTHRD103, S6966
        } while (!result.EndOfMessage);

        messageStream.Position = 0;
        return Serializer.Deserialize<AppMessage>(messageStream);
    }

    /// <summary>
    /// Raises the receive events for <paramref name="message"/> and correlates it with any pending request.
    /// A seq-bearing response resolves the request it answers; a broadcast resolves the oldest waiter whose
    /// matcher accepts it, else it is a pure notification.
    /// </summary>
    /// <param name="message">The message just read off the socket.</param>
    private void DispatchMessage(AppMessage message)
    {
        Logger.LogReceivedMessage(message);
        MessageReceived?.Invoke(this, message);

        if (message.Broadcast is not null)
        {
            Logger.LogReceivedNotification(message);
            NotificationReceived?.Invoke(this, message);
            // Entity type from message.Response.EntityInfo.Type is not yet used.
            ParseNotification(message.Broadcast);
        }
        else
        {
            Logger.LogReceivedResponse(message);
            ResponseReceived?.Invoke(this, message);
        }

        if (message.Response is not null)
        {
            if (_pendingRequests.TryRemove(message.Response.Seq, out var tcs))
            {
                tcs.TrySetResult(message);
            }
        }
        else if (message.Broadcast is not null)
        {
            ResolveBroadcastReply(message);
        }
    }

    /// <summary>
    /// Faults every in-flight request (seq-keyed and broadcast-reply waiters) with
    /// <paramref name="reason"/>. Called when the transport dies or closes: at that point no pending
    /// request can ever be answered, so failing fast beats letting each one hit its full timeout.
    /// Waiters that already completed (cancelled, timed out, resolved) are unaffected.
    /// </summary>
    /// <param name="reason">The transport failure to surface to each pending caller.</param>
    private void FailPendingRequests(Exception reason)
    {
        foreach (var seq in _pendingRequests.Keys.ToArray())
        {
            if (_pendingRequests.TryRemove(seq, out var tcs))
            {
                tcs.TrySetException(reason);
            }
        }

        lock (_broadcastRepliesLock)
        {
            foreach (var (_, tcs) in _pendingBroadcastReplies)
            {
                tcs.TrySetException(reason);
            }

            _pendingBroadcastReplies.Clear();
        }
    }

    /// <summary>
    /// Resolves an incoming broadcast against the pending broadcast-reply waiters: the oldest live
    /// waiter whose matcher accepts the broadcast wins. A broadcast that matches no waiter is a pure
    /// notification and is left untouched (it was already dispatched via <see cref="ParseNotification"/>).
    /// </summary>
    /// <param name="message">The received <see cref="AppMessage"/> carrying a non-null broadcast.</param>
    private void ResolveBroadcastReply(AppMessage message)
    {
        lock (_broadcastRepliesLock)
        {
            var index = 0;
            while (index < _pendingBroadcastReplies.Count)
            {
                var (matches, tcs) = _pendingBroadcastReplies[index];
                if (!SafeMatches(matches, message.Broadcast))
                {
                    index++;
                    continue;
                }

                _pendingBroadcastReplies.RemoveAt(index);

                // RunContinuationsAsynchronously means no awaiter runs inline under this lock.
                // A false result is a waiter already cancelled/timed-out: keep scanning.
                if (tcs.TrySetResult(message))
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Runs a broadcast-reply matcher defensively: a throwing matcher (from a derived class) must count
    /// as "no match", never kill the receive loop.
    /// </summary>
    /// <param name="matches">The matcher supplied by the requester.</param>
    /// <param name="broadcast">The broadcast under consideration.</param>
    private bool SafeMatches(Func<AppBroadcast, bool> matches, AppBroadcast broadcast)
    {
        try
        {
            return matches(broadcast);
        }
        catch (Exception ex)
        {
            Logger.LogMatcherThrew(ex);
            return false;
        }
    }
}
