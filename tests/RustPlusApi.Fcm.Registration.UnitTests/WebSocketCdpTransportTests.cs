using Xunit;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using RustPlusApi.Fcm.Registration.Steps.Cdp;

namespace RustPlusApi.Fcm.Registration.UnitTests;

public class WebSocketCdpTransportTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    private static async Task<(WebSocket Client, WebSocket Server, TcpClient ClientTcp, TcpClient ServerTcp)>
        CreatePairAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var clientTcp = new TcpClient();
            var accept = listener.AcceptTcpClientAsync();
            await clientTcp.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            var serverTcp = await accept;
            var keepAlive = TimeSpan.Zero;
            return (
                WebSocket.CreateFromStream(clientTcp.GetStream(), false, null, keepAlive),
                WebSocket.CreateFromStream(serverTcp.GetStream(), true, null, keepAlive),
                clientTcp,
                serverTcp);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ReceiveAsync_MessageInThreeFragments_IsReassembledIntoOneString()
    {
        var (client, server, clientTcp, serverTcp) = await CreatePairAsync();
        using (clientTcp)
        using (serverTcp)
        using (server)
        {
            await using var transport = new WebSocketCdpTransport(client);
            await server.SendAsync(Encoding.UTF8.GetBytes("{\"a\":"), WebSocketMessageType.Text, false,
                CancellationToken.None);
            await server.SendAsync(Encoding.UTF8.GetBytes("\"hé"), WebSocketMessageType.Text, false,
                CancellationToken.None);
            await server.SendAsync(Encoding.UTF8.GetBytes("llo\"}"), WebSocketMessageType.Text, true,
                CancellationToken.None);

            var message = await transport.ReceiveAsync(CancellationToken.None).WaitAsync(TestTimeout);

            Assert.Equal("{\"a\":\"héllo\"}", message);
        }
    }

    [Fact]
    public async Task ReceiveAsync_ServerCloses_ReturnsNull()
    {
        var (client, server, clientTcp, serverTcp) = await CreatePairAsync();
        using (clientTcp)
        using (serverTcp)
        using (server)
        {
            await using var transport = new WebSocketCdpTransport(client);
            await server.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);

            Assert.Null(await transport.ReceiveAsync(CancellationToken.None).WaitAsync(TestTimeout));
        }
    }

    [Fact]
    public async Task ReceiveAsync_ConnectionDropsWithoutCloseFrame_ReturnsNull()
    {
        var (client, server, clientTcp, serverTcp) = await CreatePairAsync();
        using (clientTcp)
        using (server)
        {
            await using var transport = new WebSocketCdpTransport(client);
            serverTcp.Dispose();

            Assert.Null(await transport.ReceiveAsync(CancellationToken.None).WaitAsync(TestTimeout));
        }
    }

    [Fact]
    public async Task Session_SendAfterPeerCloses_ThrowsCdpExceptionAndLeavesNoUnobservedTask()
    {
        var unobserved = new List<Exception>();
        void OnUnobserved(object? s, UnobservedTaskExceptionEventArgs e) => unobserved.Add(e.Exception);
        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            var (client, server, clientTcp, serverTcp) = await CreatePairAsync();
            using (clientTcp)
            using (serverTcp)
            using (server)
            {
                await using var session = new CdpSession(new WebSocketCdpTransport(client));
                await server.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                Assert.Null(await session.ReadEventAsync(CancellationToken.None).WaitAsync(TestTimeout));

                var ex = await Assert.ThrowsAsync<CdpException>(() =>
                    session.SendAsync("Browser.close", null, CancellationToken.None).WaitAsync(TestTimeout));

                Assert.Equal("The browser closed the debugging connection.", ex.Message);
            }

#pragma warning disable S1215 // Finalizers must run for UnobservedTaskException to fire at all.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
#pragma warning restore S1215
            Assert.Empty(unobserved);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    [Fact]
    public async Task SendAsync_ReachesTheServerByteExact()
    {
        var (client, server, clientTcp, serverTcp) = await CreatePairAsync();
        using (clientTcp)
        using (serverTcp)
        using (server)
        {
            await using var transport = new WebSocketCdpTransport(client);
            const string Text = "{\"id\":1,\"method\":\"hé ✓\"}";

            await transport.SendAsync(Text, CancellationToken.None).WaitAsync(TestTimeout);

            var buffer = new byte[256];
            var result = await server.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None)
                .WaitAsync(TestTimeout);
            Assert.Equal(WebSocketMessageType.Text, result.MessageType);
            Assert.True(result.EndOfMessage);
            Assert.Equal(Encoding.UTF8.GetBytes(Text), buffer.Take(result.Count).ToArray());
        }
    }
}
