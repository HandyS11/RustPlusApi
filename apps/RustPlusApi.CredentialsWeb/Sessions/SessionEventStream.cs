using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace RustPlusApi.CredentialsWeb.Sessions;

/// <summary>Fans one session's events out to every subscriber, replaying the events published
/// before each subscription. Replay is what makes an SSE reconnect resume rather than restart —
/// which matters because the drop happens exactly when the visitor alt-tabs into fullscreen Rust.</summary>
internal sealed class SessionEventStream
{
    private readonly List<SessionEvent> _history = [];
    private readonly Lock _gate = new();
    private readonly List<Channel<SessionEvent>> _subscribers = [];
    private bool _completed;

    /// <summary>Appends an event to the history and pushes it to every live subscriber.
    /// Ignored once <see cref="Complete"/> has been called.</summary>
    /// <param name="sessionEvent">The event to publish.</param>
    internal void Publish(SessionEvent sessionEvent)
    {
        lock (_gate)
        {
            if (_completed)
            {
                return;
            }

            _history.Add(sessionEvent);
            foreach (var subscriber in _subscribers)
            {
                // Unbounded channel: writes always succeed, so a slow reader can never block the flow.
                subscriber.Writer.TryWrite(sessionEvent);
            }
        }
    }

    /// <summary>Ends every subscriber's enumeration. Idempotent.</summary>
    internal void Complete()
    {
        lock (_gate)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            foreach (var subscriber in _subscribers)
            {
                subscriber.Writer.TryComplete();
            }

            _subscribers.Clear();
        }
    }

    /// <summary>Yields every event published so far, then every subsequent one until the stream is
    /// completed or <paramref name="cancellationToken"/> fires.</summary>
    /// <param name="cancellationToken">Cancellation token for the subscription.</param>
    /// <returns>An async enumerable of events.</returns>
    internal async IAsyncEnumerable<SessionEvent> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (history, reader) = Subscribe();

        foreach (var item in history)
        {
            yield return item;
        }

        if (reader is null)
        {
            yield break;
        }

        await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    /// <summary>Snapshots the history and registers a subscriber under one lock, so no event can slip
    /// between the two. Returns a null reader when the stream is already complete.</summary>
    private (IReadOnlyList<SessionEvent> History, ChannelReader<SessionEvent>? Reader) Subscribe()
    {
        lock (_gate)
        {
            var history = _history.ToArray();
            if (_completed)
            {
                return (history, null);
            }

            var channel = Channel.CreateUnbounded<SessionEvent>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

            _subscribers.Add(channel);
            return (history, channel.Reader);
        }
    }
}
