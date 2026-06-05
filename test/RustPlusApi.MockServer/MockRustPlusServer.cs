using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using Google.Protobuf;
using RustPlusContracts;

namespace RustPlusApi.MockServer;

/// <summary>
/// A minimal in-process Rust+ server that speaks the real wire protocol over a WebSocket.
/// It accepts an <see cref="AppRequest"/>, runs it through a responder, and replies with an
/// <see cref="AppMessage"/> serialized with the same contract types the library uses, so
/// request/response stay in lockstep with the schema. It can also push broadcasts on demand.
/// </summary>
public sealed class MockRustPlusServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Func<AppRequest, AppMessage?> _responder;

    private WebSocket? _activeSocket;
    private Task? _acceptLoop;

    /// <summary>The loopback port the server listens on.</summary>
    public int Port { get; }

    /// <summary>The host the client should connect to (always loopback).</summary>
    public string Host => "localhost";

    /// <summary>
    /// Creates a server on a free loopback port.
    /// </summary>
    /// <param name="responder">
    /// Maps a request to its response. Returning <see langword="null"/> sends nothing.
    /// Defaults to <see cref="MockResponses.Default"/>.
    /// </param>
    public MockRustPlusServer(Func<AppRequest, AppMessage?>? responder = null)
    {
        _responder = responder ?? MockResponses.Default;
        Port = GetFreePort();
        _listener.Prefixes.Add($"http://localhost:{Port}/");
    }

    /// <summary>Starts listening and accepting connections.</summary>
    public void Start()
    {
        _listener.Start();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    /// <summary>
    /// Pushes a broadcast to the connected client. Throws if no client is connected.
    /// </summary>
    public Task BroadcastAsync(AppBroadcast broadcast)
    {
        var socket = _activeSocket
            ?? throw new InvalidOperationException("No client is connected to the mock server.");
        return SendAsync(socket, MockResponses.Broadcast(broadcast), _cts.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                break;
            }

            if (!context.Request.IsWebSocketRequest)
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                context.Response.Close();
                continue;
            }

            var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
            _ = Task.Run(() => HandleClientAsync(wsContext.WebSocket, ct), ct);
        }
    }

    private async Task HandleClientAsync(WebSocket socket, CancellationToken ct)
    {
        _activeSocket = socket;
        var buffer = new byte[8192];

        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            try
            {
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, ct);
                        return;
                    }
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
            }
            catch (Exception) when (ct.IsCancellationRequested || socket.State != WebSocketState.Open)
            {
                return;
            }

            var request = AppRequest.Parser.ParseFrom(message.ToArray());
            var response = _responder(request);
            if (response is null) continue;

            await SendAsync(socket, response, ct);
        }
    }

    private async Task SendAsync(WebSocket socket, AppMessage message, CancellationToken ct)
    {
        var bytes = message.ToByteArray();
        await _sendLock.WaitAsync(ct);
        try
        {
            await socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Binary,
                endOfMessage: true,
                ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();

        if (_activeSocket is { State: WebSocketState.Open })
        {
            try
            {
                await _activeSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
            }
            catch (WebSocketException)
            {
                // The client may already be gone; nothing to do.
            }
        }

        if (_listener.IsListening) _listener.Stop();
        _listener.Close();

        if (_acceptLoop is not null)
        {
            try { await _acceptLoop; }
            catch (OperationCanceledException) { /* expected on shutdown */ }
        }

        _cts.Dispose();
        _sendLock.Dispose();
        _activeSocket?.Dispose();
    }
}
