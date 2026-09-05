using RustPlusApi.CredentialsWeb.Sessions;
using Xunit;

namespace RustPlusApi.CredentialsWeb.UnitTests;

public sealed class SessionEventStreamTests
{
    private static async Task<List<SessionEvent>> DrainAsync(SessionEventStream stream, int expected)
    {
        var received = new List<SessionEvent>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await foreach (var item in stream.SubscribeAsync(timeout.Token))
        {
            received.Add(item);
            if (received.Count == expected)
            {
                break;
            }
        }

        return received;
    }

    [Fact]
    public async Task SubscribeAsync_ReplaysEventsPublishedBeforeSubscription()
    {
        var stream = new SessionEventStream();
        stream.Publish(new SessionEvent("step", new StepPayload("Registering")));
        stream.Publish(new SessionEvent("step", new StepPayload("Ready")));

        var received = await DrainAsync(stream, 2);

        Assert.Equal(2, received.Count);
        Assert.All(received, e => Assert.Equal("step", e.Type));
    }

    [Fact]
    public async Task SubscribeAsync_DeliversEventsPublishedAfterSubscription()
    {
        var stream = new SessionEventStream();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var enumerator = stream.SubscribeAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);

        stream.Publish(new SessionEvent("paired", null));

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("paired", enumerator.Current.Type);
        await enumerator.DisposeAsync();
    }

    [Fact]
    public async Task SubscribeAsync_ReplaysThenStreams_InOrder()
    {
        var stream = new SessionEventStream();
        stream.Publish(new SessionEvent("first", null));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var enumerator = stream.SubscribeAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("first", enumerator.Current.Type);

        stream.Publish(new SessionEvent("second", null));

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("second", enumerator.Current.Type);
        await enumerator.DisposeAsync();
    }

    [Fact]
    public async Task SubscribeAsync_SupportsTwoConcurrentSubscribers()
    {
        var stream = new SessionEventStream();
        var first = DrainAsync(stream, 1);
        var second = DrainAsync(stream, 1);

        // Give both subscribers a chance to register before publishing.
        await Task.Delay(50);
        stream.Publish(new SessionEvent("step", null));

        Assert.Single(await first);
        Assert.Single(await second);
    }

    [Fact]
    public async Task SubscribeAsync_CompletesWhenStreamCompleted()
    {
        var stream = new SessionEventStream();
        stream.Publish(new SessionEvent("expired", null));
        stream.Complete();

        var received = new List<SessionEvent>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var item in stream.SubscribeAsync(timeout.Token))
        {
            received.Add(item);
        }

        Assert.Single(received);
    }

    [Fact]
    public async Task Publish_AfterComplete_IsIgnored()
    {
        var stream = new SessionEventStream();
        stream.Complete();
        stream.Publish(new SessionEvent("step", null));

        var received = new List<SessionEvent>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var item in stream.SubscribeAsync(timeout.Token))
        {
            received.Add(item);
        }

        Assert.Empty(received);
    }

    [Fact]
    public async Task SubscribeAsync_DeregistersSubscriberWhenEnumerationEnds()
    {
        var stream = new SessionEventStream();
        stream.Publish(new SessionEvent("first", null));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var enumerator = stream.SubscribeAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("first", enumerator.Current.Type);
        Assert.Equal(1, stream.SubscriberCount);

        // Cancel to end the enumeration
        await timeout.CancelAsync();

        try
        {
            await enumerator.MoveNextAsync();
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        await enumerator.DisposeAsync();

        // Subscriber should be deregistered after enumeration ends
        Assert.Equal(0, stream.SubscriberCount);

        // Publishing after deregistration should not affect the removed subscriber
        stream.Publish(new SessionEvent("second", null));
    }
}
