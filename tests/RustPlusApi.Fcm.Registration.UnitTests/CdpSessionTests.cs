using Xunit;
using System.Text.Json.Nodes;
using RustPlusApi.Fcm.Registration.Steps.Cdp;
using RustPlusApi.Fcm.Registration.UnitTests.TestHelpers;

namespace RustPlusApi.Fcm.Registration.UnitTests;

public class CdpSessionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task SendAsync_FirstCommand_HasExactWireFormatAndIdsIncrease()
    {
        var transport = new FakeCdpTransport();
        await using var session = new CdpSession(transport);

        await session.SendAsync("Page.enable", null, CancellationToken.None).WaitAsync(TestTimeout);
        await session.SendAsync("Page.navigate", new JsonObject
        {
            ["url"] = "x"
        }, CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Equal("""{"id":1,"method":"Page.enable","params":{}}""", transport.Sent[0].ToJsonString());
        Assert.Equal("""{"id":2,"method":"Page.navigate","params":{"url":"x"}}""", transport.Sent[1].ToJsonString());
    }

    [Fact]
    public async Task SendAsync_RepliesOutOfOrder_AreMatchedById()
    {
        var bothSent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new FakeCdpTransport
        {
            AutoReply = false
        };
        transport.OnCommand = _ =>
        {
            if (transport.Sent.Count == 2)
            {
                bothSent.SetResult(true);
            }
        };
        await using var session = new CdpSession(transport);

        var first = session.SendAsync("A", null, CancellationToken.None);
        var second = session.SendAsync("B", null, CancellationToken.None);
        await bothSent.Task.WaitAsync(TestTimeout);
        transport.Push("""{"id":2,"result":{"v":"b"}}""");
        transport.Push("""{"id":1,"result":{"v":"a"}}""");

        Assert.Equal("a", (string?)(await first.WaitAsync(TestTimeout))!["v"]);
        Assert.Equal("b", (string?)(await second.WaitAsync(TestTimeout))!["v"]);
    }

    [Fact]
    public async Task SendAsync_ReplyWithoutResult_ReturnsNull()
    {
        var transport = new FakeCdpTransport
        {
            AutoReply = false
        };
        await using var session = new CdpSession(transport);
        var pending = session.SendAsync("A", null, CancellationToken.None);
        transport.Push("""{"id":1}""");

        Assert.Null(await pending.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task SendAsync_ErrorReply_ThrowsCdpExceptionWithMessage()
    {
        var transport = new FakeCdpTransport();
        transport.FailCommand("Bad.method", "boom");
        await using var session = new CdpSession(transport);

        var ex = await Assert.ThrowsAsync<CdpException>(() =>
            session.SendAsync("Bad.method", null, CancellationToken.None).WaitAsync(TestTimeout));

        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public async Task SendAsync_ErrorReplyWithoutMessage_ThrowsGenericCdpException()
    {
        var transport = new FakeCdpTransport
        {
            AutoReply = false
        };
        await using var session = new CdpSession(transport);
        var pending = session.SendAsync("A", null, CancellationToken.None);
        transport.Push("""{"id":1,"error":{"code":-1}}""");

        var ex = await Assert.ThrowsAsync<CdpException>(() => pending.WaitAsync(TestTimeout));

        Assert.Equal("The browser reported an error.", ex.Message);
    }

    [Fact]
    public async Task ReadEventAsync_ReturnsEventsInOrder_AndRepliesAreNotEvents()
    {
        var transport = new FakeCdpTransport();
        await using var session = new CdpSession(transport);
        transport.Push("""{"method":"First","params":{"n":1}}""");
        transport.Push("""{"method":"Second"}""");
        await session.SendAsync("Page.enable", null, CancellationToken.None).WaitAsync(TestTimeout);
        transport.Push("""{"method":"Third","params":{"n":3}}""");

        var first = await session.ReadEventAsync(CancellationToken.None).WaitAsync(TestTimeout);
        var second = await session.ReadEventAsync(CancellationToken.None).WaitAsync(TestTimeout);
        var third = await session.ReadEventAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Equal("First", first!.Value.Method);
        Assert.Equal(1, (int)first.Value.Params!["n"]!);
        Assert.Equal("Second", second!.Value.Method);
        Assert.Null(second.Value.Params);
        Assert.Equal("Third", third!.Value.Method);
    }

    [Fact]
    public async Task ReadEventAsync_AfterOnCommandPushesEvent_DeliversIt()
    {
        var transport = new FakeCdpTransport();
        transport.OnCommand = cmd => transport.Push($$"""{"method":"Done:{{(string)cmd["method"]!}}"}""");
        await using var session = new CdpSession(transport);

        await session.SendAsync("Page.navigate", null, CancellationToken.None).WaitAsync(TestTimeout);
        var evt = await session.ReadEventAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Equal("Done:Page.navigate", evt!.Value.Method);
    }

    [Fact]
    public async Task Close_WakesEveryConcurrentReader()
    {
        var transport = new FakeCdpTransport();
        await using var session = new CdpSession(transport);
        var readers = Enumerable.Range(0, 3)
            .Select(_ => session.ReadEventAsync(CancellationToken.None))
            .ToArray();

        // Let every reader block on the event signal before the connection closes.
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        Assert.All(readers, reader => Assert.False(reader.IsCompleted));

        transport.Close();

        foreach (var reader in readers)
        {
            Assert.Null(await reader.WaitAsync(TestTimeout));
        }
    }

    [Fact]
    public async Task Close_WithoutFault_ClosedExceptionHasNoInnerException()
    {
        var sent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new FakeCdpTransport
        {
            AutoReply = false, OnCommand = _ => sent.SetResult(true)
        };
        await using var session = new CdpSession(transport);
        var pending = session.SendAsync("A", null, CancellationToken.None);
        await sent.Task.WaitAsync(TestTimeout);

        transport.Close();

        var ex = await Assert.ThrowsAsync<CdpException>(() => pending.WaitAsync(TestTimeout));
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task Close_PendingSendThrows_AndReadEventDrainsThenReturnsNull()
    {
        var sent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new FakeCdpTransport
        {
            AutoReply = false, OnCommand = _ => sent.SetResult(true)
        };
        await using var session = new CdpSession(transport);
        var pending = session.SendAsync("A", null, CancellationToken.None);
        await sent.Task.WaitAsync(TestTimeout);
        transport.Push("""{"method":"Queued"}""");

        transport.Close();

        var ex = await Assert.ThrowsAsync<CdpException>(() => pending.WaitAsync(TestTimeout));
        Assert.Equal("The browser closed the debugging connection.", ex.Message);
        Assert.Equal("Queued",
            (await session.ReadEventAsync(CancellationToken.None).WaitAsync(TestTimeout))!.Value.Method);
        Assert.Null(await session.ReadEventAsync(CancellationToken.None).WaitAsync(TestTimeout));
        Assert.Null(await session.ReadEventAsync(CancellationToken.None).WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task IsClosed_FalseWhileOpen_TrueOnceTheConnectionCloses()
    {
        var transport = new FakeCdpTransport();
        await using var session = new CdpSession(transport);
        Assert.False(session.IsClosed);

        transport.Close();
        Assert.Null(await session.ReadEventAsync(CancellationToken.None).WaitAsync(TestTimeout));

        Assert.True(session.IsClosed);
    }

    [Fact]
    public async Task SendAsync_AfterConnectionClosed_ThrowsCdpException()
    {
        var transport = new FakeCdpTransport();
        await using var session = new CdpSession(transport);
        transport.Close();
        Assert.Null(await session.ReadEventAsync(CancellationToken.None).WaitAsync(TestTimeout));

        var ex = await Assert.ThrowsAsync<CdpException>(() =>
            session.SendAsync("A", null, CancellationToken.None).WaitAsync(TestTimeout));

        Assert.Equal("The browser closed the debugging connection.", ex.Message);
    }

    [Fact]
    public async Task SendAsync_TransportSendFails_ThrowsCdpExceptionWithCause()
    {
        var cause = new InvalidOperationException("socket gone");
        var transport = new FakeCdpTransport
        {
            SendFailure = cause
        };
        await using var session = new CdpSession(transport);

        var ex = await Assert.ThrowsAsync<CdpException>(() =>
            session.SendAsync("A", null, CancellationToken.None).WaitAsync(TestTimeout));

        Assert.Equal("The browser closed the debugging connection.", ex.Message);
        Assert.Same(cause, ex.InnerException);
    }

    [Fact]
    public async Task SendAsync_TransportSendCancelled_PropagatesCancellation()
    {
        var transport = new FakeCdpTransport
        {
            SendFailure = new OperationCanceledException()
        };
        await using var session = new CdpSession(transport);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            session.SendAsync("A", null, CancellationToken.None).WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task SendAsync_PeerClosedButLoopNotYetObserved_ThrowsCdpException()
    {
        var transport = new FakeCdpTransport();
        await using var session = new CdpSession(transport);
        transport.Close();

        // The fake (like a real socket) rejects the send itself; the session must wrap it, whichever side wins the race.
        var ex = await Assert.ThrowsAsync<CdpException>(() =>
            session.SendAsync("A", null, CancellationToken.None).WaitAsync(TestTimeout));

        Assert.Equal("The browser closed the debugging connection.", ex.Message);
    }

    [Fact]
    public async Task ReadEventAsync_EventQueuedJustBeforeClose_IsNeverLost()
    {
        for (var i = 0; i < 200; i++)
        {
            var transport = new FakeCdpTransport();
            await using var session = new CdpSession(transport);
            transport.Push("""{"method":"Last"}""");
            transport.Close();

            var evt = await session.ReadEventAsync(CancellationToken.None).WaitAsync(TestTimeout);

            Assert.Equal("Last", evt!.Value.Method);
            Assert.Null(await session.ReadEventAsync(CancellationToken.None).WaitAsync(TestTimeout));
        }
    }

    [Fact]
    public async Task ReadLoop_TransportThrowsNonWebSocketException_IsTreatedAsClosedAndDisposeDoesNotThrow()
    {
        var cause = new IOException("boom");
        var transport = new FakeCdpTransport
        {
            AutoReply = false, ReceiveFailure = cause
        };
        var session = new CdpSession(transport);

        Assert.Null(await session.ReadEventAsync(CancellationToken.None).WaitAsync(TestTimeout));
        var ex = await Assert.ThrowsAsync<CdpException>(() =>
            session.SendAsync("A", null, CancellationToken.None).WaitAsync(TestTimeout));
        Assert.Same(cause, ex.InnerException);

        await session.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        Assert.True(transport.Disposed);
    }

    [Fact]
    public async Task SendAsync_PendingWhenTransportFaults_ThrowsCdpExceptionWithCause()
    {
        var sent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cause = new IOException("boom");
        var transport = new FakeCdpTransport
        {
            AutoReply = false, OnCommand = _ => sent.SetResult(true)
        };
        await using var session = new CdpSession(transport);
        var pending = session.SendAsync("A", null, CancellationToken.None);
        await sent.Task.WaitAsync(TestTimeout);

        transport.ReceiveFailure = cause;
        transport.Push("""{"method":"Wake"}""");

        var ex = await Assert.ThrowsAsync<CdpException>(() => pending.WaitAsync(TestTimeout));
        Assert.Same(cause, ex.InnerException);
    }

    [Fact]
    public async Task ReadEventAsync_Cancelled_ThrowsOperationCanceledException()
    {
        await using var session = new CdpSession(new FakeCdpTransport());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            session.ReadEventAsync(cts.Token).WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task SendAsync_CancelledWhilePending_ThrowsOperationCanceledException()
    {
        var sent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new FakeCdpTransport
        {
            AutoReply = false, OnCommand = _ => sent.SetResult(true)
        };
        await using var session = new CdpSession(transport);
        using var cts = new CancellationTokenSource();
        var pending = session.SendAsync("A", null, cts.Token);
        await sent.Task.WaitAsync(TestTimeout);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task ReadLoop_IgnoresMalformedAndUnroutableMessages()
    {
        var transport = new FakeCdpTransport();
        await using var session = new CdpSession(transport);
        transport.Push("not json");
        transport.Push("[]");
        transport.Push("""{"id":"x"}""");
        transport.Push("""{"id":99,"result":{}}""");
        transport.Push("{}");
        transport.Push("""{"method":5}""");
        transport.Push("""{"method":"Real"}""");

        var evt = await session.ReadEventAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Equal("Real", evt!.Value.Method);
    }

    [Fact]
    public async Task DisposeAsync_StopsTheReadLoop()
    {
        var session = new CdpSession(new FakeCdpTransport());

        await session.DisposeAsync().AsTask().WaitAsync(TestTimeout);

        Assert.Null(await session.ReadEventAsync(CancellationToken.None).WaitAsync(TestTimeout));
    }

    [Fact]
    public void CdpException_HasTheThreeStandardConstructors()
    {
        var inner = new InvalidOperationException("inner");

        Assert.NotNull(new CdpException().Message);
        Assert.Equal("m", new CdpException("m").Message);
        var both = new CdpException("m", inner);
        Assert.Same(inner, both.InnerException);
        Assert.Equal("m", both.Message);
    }
}
