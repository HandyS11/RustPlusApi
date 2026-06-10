using Microsoft.Extensions.Logging;

namespace RustPlusApi;

/// <summary>
/// Tuning options for <see cref="RustPlusSocket"/>. All values have sensible defaults; pass an
/// instance to the <see cref="RustPlus"/> constructor only when the defaults don't fit (slow
/// servers, aggressive NATs, latency-sensitive tooling). Properties are init-only: configure the
/// instance at construction, then share it freely — it is never mutated by the client.
/// </summary>
public sealed class RustPlusSocketOptions
{
    /// <summary>How long a request waits for its response before faulting with a
    /// <see cref="TimeoutException"/>. Default: 30 seconds.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The WebSocket keep-alive ping interval. Default: 20 seconds.</summary>
    public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>Bounds teardown waits (graceful close, loop shutdown, pending-request drain) so a
    /// wedged loop or dead peer can never hang disposal. Default: 5 seconds.</summary>
    public TimeSpan TeardownTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The receive buffer size in bytes. Map images arrive as multi-megabyte messages, so
    /// a larger buffer means far fewer reads per message. Default: 64 KB.</summary>
    public int ReceiveBufferSize { get; init; } = 64 * 1024;

    /// <summary>Factory used to create the client's logger. When <see langword="null"/>, logging is
    /// disabled (a no-op <c>NullLogger</c> is used). Supply one to route diagnostics into your
    /// logging stack.</summary>
    public ILoggerFactory? LoggerFactory { get; init; }
}
