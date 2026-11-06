using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RustPlusApi.Fcm.Registration.Steps.Cdp;

/// <summary>
/// A minimal Chrome DevTools Protocol client: matches command replies to their callers by id and queues events.
/// A background read loop starts on construction and ends when the transport closes or the session is disposed.
/// </summary>
internal sealed class CdpSession : IAsyncDisposable
{
    private const string ClosedMessage = "The browser closed the debugging connection.";
    private readonly SemaphoreSlim _eventSignal = new(0);
    private readonly ConcurrentQueue<CdpEvent> _events = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly Task _readLoop;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _stop = new();

    private readonly ICdpTransport _transport;

    /// <summary>0 while open, 1 once the read loop has ended. Written once with <see cref="Interlocked.Exchange(ref int, int)"/>
    /// (a full fence) and read with <see cref="Volatile.Read(ref int)"/>, so the loop's close-then-sweep and
    /// <see cref="SendAsync"/>'s add-then-check cannot pass each other.</summary>
    private int _closed;

    private Exception? _fault;
    private int _nextId;

    /// <summary>Initializes a new instance of the <see cref="CdpSession"/> class and starts reading.</summary>
    /// <param name="transport">The transport; the session owns and disposes it.</param>
    public CdpSession(ICdpTransport transport)
    {
        _transport = transport;
        _readLoop = Task.Run(RunReadLoopAsync, CancellationToken.None);
    }

    /// <summary>Whether the connection has closed (the read loop ended). Distinguishes a browser error reply
    /// from the window going away when a <see cref="CdpException"/> is caught.</summary>
    internal bool IsClosed => Volatile.Read(ref _closed) != 0;

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
#pragma warning disable CA1849, VSTHRD103, S6966 // CancelAsync does not exist on netstandard2.0.
            _stop.Cancel();
#pragma warning restore CA1849, VSTHRD103, S6966
#pragma warning disable VSTHRD003 // The read loop is started by this class's own constructor, so awaiting it cannot deadlock a foreign context.
            await _readLoop.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        finally
        {
            await _transport.DisposeAsync().ConfigureAwait(false);
            _stop.Dispose();
            _sendLock.Dispose();
            _eventSignal.Dispose();
        }
    }

    /// <summary>Sends a command and waits for its reply.</summary>
    /// <param name="method">The CDP method, e.g. <c>Page.enable</c>.</param>
    /// <param name="parameters">The command parameters; <see langword="null"/> sends an empty object.</param>
    /// <param name="cancellationToken">Cancels the wait for the reply.</param>
    /// <returns>The reply's <c>result</c> node, or <see langword="null"/> when it had none.</returns>
    /// <exception cref="CdpException">The browser replied with an error, or the connection closed first.</exception>
    public async Task<JsonNode?> SendAsync(string method, JsonObject? parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var reply = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = reply;
        try
        {
            // The fence keeps the read of the flag after the add above. Paired with the read loop's fenced close
            // before its sweep of _pending, one side always sees the other: either the sweep fails this reply, or this
            // check sees the close. Without it the two could be reordered and the reply would never complete.
            Thread.MemoryBarrier();
            if (IsClosed)
            {
                throw ClosedException();
            }

            var command = new JsonObject
            {
                ["id"] = id, ["method"] = method, ["params"] = parameters ?? new JsonObject()
            };
            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _transport.SendAsync(command.ToJsonString(), cancellationToken).ConfigureAwait(false);
            }
#pragma warning disable CA1031, S2221 // Any send failure other than cancellation means the connection is unusable.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031, S2221
            {
                throw ClosedException(ex);
            }
            finally
            {
                _sendLock.Release();
            }

            using (cancellationToken.Register(() => reply.TrySetCanceled(cancellationToken)))
            {
                return await reply.Task.ConfigureAwait(false);
            }
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>Waits for the next event.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The event, or <see langword="null"/> once the connection is closed and queued events are drained.</returns>
    public async Task<CdpEvent?> ReadEventAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            // Snapshot first: the read loop sets _closed only after its last enqueue, so a failed dequeue
            // following a true snapshot proves the queue is drained.
            var closed = IsClosed;
            if (_events.TryDequeue(out var evt))
            {
                return evt;
            }

            if (closed)
            {
                return null;
            }

            await _eventSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunReadLoopAsync()
    {
        try
        {
            string? message;
            while ((message = await _transport.ReceiveAsync(_stop.Token).ConfigureAwait(false)) is not null)
            {
                Dispatch(message);
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed while waiting for the next message: fall through to the closed handling.
        }
#pragma warning disable CA1031, S2221 // A failing transport must end the session as "closed", never fault the loop.
        catch (Exception ex)
#pragma warning restore CA1031, S2221
        {
            _fault = ex;
        }
        finally
        {
            // A full fence: the flag is visible before _pending is swept (see SendAsync).
            Interlocked.Exchange(ref _closed, 1);
            foreach (var reply in _pending.Values)
            {
                reply.TrySetException(ClosedException());
            }

            _eventSignal.Release();
        }
    }

    private CdpException ClosedException(Exception? cause = null) => new(ClosedMessage, cause ?? _fault!);

    private void Dispatch(string message)
    {
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(message) as JsonObject;
        }
        catch (JsonException)
        {
            return;
        }

        if (root?["id"] is JsonValue idValue)
        {
            if (idValue.TryGetValue(out int id) && _pending.TryRemove(id, out var reply))
            {
                Complete(reply, root);
            }
        }
        else if (root?["method"] is JsonValue methodValue && methodValue.TryGetValue(out string? method))
        {
            _events.Enqueue(new CdpEvent(method, root["params"]));
            _eventSignal.Release();
        }
    }

    private static void Complete(TaskCompletionSource<JsonNode?> reply, JsonObject root)
    {
        if (root["error"] is JsonObject error)
        {
            reply.TrySetException(new CdpException(error["message"]?.ToString() ?? "The browser reported an error."));
        }
        else
        {
            reply.TrySetResult(root["result"]);
        }
    }
}
