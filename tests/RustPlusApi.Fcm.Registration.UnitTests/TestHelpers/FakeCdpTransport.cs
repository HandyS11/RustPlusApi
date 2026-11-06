using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json.Nodes;
using RustPlusApi.Fcm.Registration.Steps.Cdp;

namespace RustPlusApi.Fcm.Registration.UnitTests.TestHelpers;

/// <summary>
/// An in-memory <see cref="ICdpTransport"/>: records every command, auto-replies <c>{"id":N,"result":{}}</c>
/// (or an error for methods registered with <see cref="FailCommand"/>) and lets a test push raw incoming messages.
/// </summary>
internal sealed class FakeCdpTransport : ICdpTransport
{
    private readonly Dictionary<string, string> _failures = [];
    private readonly ConcurrentQueue<string> _incoming = new();
    private readonly SemaphoreSlim _signal = new(0);
    private volatile bool _closed;

    /// <summary>Every command sent, parsed, in order.</summary>
    public List<JsonObject> Sent { get; } = [];

    /// <summary>Invoked after each command (and its auto-reply, if any), so a test can push events in response.</summary>
    public Action<JsonObject>? OnCommand { get; set; }

    /// <summary>When <see langword="false"/>, commands are recorded but never answered; the test pushes the replies itself.</summary>
    public bool AutoReply { get; set; } = true;

    /// <summary>When set, <see cref="SendAsync"/> throws this instead of recording the command.</summary>
    public Exception? SendFailure { get; set; }

    /// <summary>When set, <see cref="ReceiveAsync"/> throws this once the incoming queue is empty.</summary>
    public Exception? ReceiveFailure { get; set; }

    /// <summary>Whether <see cref="DisposeAsync"/> has been called.</summary>
    public bool Disposed { get; private set; }

    /// <inheritdoc/>
    public Task SendAsync(string message, CancellationToken cancellationToken)
    {
        if (SendFailure is not null)
        {
            throw SendFailure;
        }

        if (_closed)
        {
            // Like a real socket after the peer closed: sending is an error, not a no-op.
            throw new WebSocketException("The remote party closed the WebSocket connection.");
        }

        var command = (JsonObject)JsonNode.Parse(message)!;
        Sent.Add(command);
        var id = (int)command["id"]!;
        if (AutoReply)
        {
            Push(_failures.TryGetValue((string)command["method"]!, out var error)
                ? new JsonObject
                {
                    ["id"] = id,
                    ["error"] = new JsonObject
                    {
                        ["code"] = -32000, ["message"] = error
                    }
                }.ToJsonString()
                : new JsonObject
                {
                    ["id"] = id, ["result"] = new JsonObject()
                }.ToJsonString());
        }

        OnCommand?.Invoke(command);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var closed = _closed;
            if (_incoming.TryDequeue(out var message))
            {
                return message;
            }

            if (ReceiveFailure is not null)
            {
                throw ReceiveFailure;
            }

            if (closed)
            {
                return null;
            }

            await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Disposed = true;
        _signal.Dispose();
        return default;
    }

    /// <summary>Delivers an incoming message to the reader.</summary>
    /// <param name="json">The raw message.</param>
    public void Push(string json)
    {
        _incoming.Enqueue(json);
        _signal.Release();
    }

    /// <summary>Makes the auto-reply to <paramref name="method"/> an error with <paramref name="message"/>.</summary>
    /// <param name="method">The CDP method name.</param>
    /// <param name="message">The error message to reply with.</param>
    public void FailCommand(string method, string message) => _failures[method] = message;

    /// <summary>Simulates the browser closing the connection: queued messages drain, then the reader sees the end.</summary>
    public void Close()
    {
        _closed = true;
        _signal.Release();
    }
}
