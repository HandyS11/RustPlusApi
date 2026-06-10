using RustPlusApi.MockServer;
using Xunit;

namespace RustPlusApi.IntegrationTests;

/// <summary>
/// Teardown guarantees: disposing a connected client must deterministically stop every
/// tracked background loop within a bounded time, with the instance token interrupting in-flight I/O.
/// </summary>
public class SocketTeardownTests
{
    private const ulong PlayerId = 76561198000000000;
    private const int PlayerToken = 123456789;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task DisposeAsync_WhileConnected_StopsLoopsPromptly()
    {
        await using var server = new MockRustPlusServer();
        server.Start();
        var client = new RustPlus(new RustPlusConnection(MockRustPlusServer.Host, server.Port, PlayerId, PlayerToken));
        await client.ConnectAsync().WaitAsync(Timeout);
        await client.GetInfoAsync().WaitAsync(Timeout);

        Assert.True(client.IsConnected);

        // DisposeAsync must cancel the instance token and await the tracked loops within a bounded time.
        var disposeTask = client.DisposeAsync().AsTask();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(disposeTask.IsCompletedSuccessfully);
        Assert.False(client.IsConnected);

        // The loops must have actually run to completion, not merely been abandoned by a timeout.
        Assert.True(client.ReceiveLoopForTests!.IsCompleted);
        Assert.True(client.SendLoopForTests!.IsCompleted);
    }
}
