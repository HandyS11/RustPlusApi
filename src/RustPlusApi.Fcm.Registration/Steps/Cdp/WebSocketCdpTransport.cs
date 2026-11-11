using System.Diagnostics.CodeAnalysis;
using System.Net.WebSockets;
using System.Text;

namespace RustPlusApi.Fcm.Registration.Steps.Cdp;

/// <summary>An <see cref="ICdpTransport"/> over a connected <see cref="WebSocket"/>.</summary>
/// <param name="socket">The connected socket; the transport owns and disposes it.</param>
internal sealed class WebSocketCdpTransport(WebSocket socket) : ICdpTransport
{
    private const int BufferSize = 16 * 1024;

    /// <inheritdoc/>
    public Task SendAsync(string message, CancellationToken cancellationToken) =>
        socket.SendAsync(
            new ArraySegment<byte>(Encoding.UTF8.GetBytes(message)),
            WebSocketMessageType.Text,
            true,
            cancellationToken);

    /// <inheritdoc/>
    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
#pragma warning disable RCS1261 // MemoryStream has no async disposal on netstandard2.0 and disposing it is a no-op anyway.
        using var message = new MemoryStream();
#pragma warning restore RCS1261
        try
        {
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken)
                    .ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

#pragma warning disable CA1849, VSTHRD103, S6966 // Writing to a MemoryStream never blocks; netstandard2.0 lacks the Memory-based async overload.
                message.Write(buffer, 0, result.Count);
#pragma warning restore CA1849, VSTHRD103, S6966
            } while (!result.EndOfMessage);
        }
        catch (WebSocketException)
        {
            return null;
        }

        return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        socket.Dispose();
        return default;
    }

    /// <summary>Opens a client WebSocket to a DevTools page endpoint.</summary>
    /// <param name="uri">The page's <c>webSocketDebuggerUrl</c>.</param>
    /// <param name="cancellationToken">Cancels the connect.</param>
    [ExcludeFromCodeCoverage]
    public static async Task<WebSocketCdpTransport> ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
#pragma warning disable CA2000 // Ownership passes to the returned transport; disposed on failure below.
        var client = new ClientWebSocket();
#pragma warning restore CA2000
        try
        {
            // Chromium sends no pongs for client pings on this endpoint; an idle keep-alive would drop the socket.
            client.Options.KeepAliveInterval = TimeSpan.Zero;
            await client.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
            return new WebSocketCdpTransport(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}
