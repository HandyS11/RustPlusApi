namespace RustPlusApi;

/// <summary>
/// Tuning options for <see cref="RustPlusSocket"/>. All values have sensible defaults; pass an
/// instance to the <see cref="RustPlus"/> constructor only when the defaults don't fit (slow
/// servers, aggressive NATs, latency-sensitive tooling). Configure the instance at construction
/// and treat it as fixed afterwards — the client never mutates it.
/// </summary>
public sealed class RustPlusSocketOptions
{
    /// <summary>How long a request waits for its response before faulting with a
    /// <see cref="TimeoutException"/>. Default: 30 seconds.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The WebSocket keep-alive ping interval. Default: 20 seconds.</summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Bounds teardown waits (graceful close, loop shutdown, pending-request drain) so a
    /// wedged loop or dead peer can never hang disposal. Default: 5 seconds.</summary>
    public TimeSpan TeardownTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The receive buffer size in bytes. Map images arrive as multi-megabyte messages, so
    /// a larger buffer means far fewer reads per message. Default: 64 KB.</summary>
    public int ReceiveBufferSize { get; set; } = 64 * 1024;

    /// <summary>Creates a copy of this instance — the single place that knows every option, so the
    /// per-socket snapshot cannot silently miss a newly added property.</summary>
    internal RustPlusSocketOptions Clone() => new()
    {
        RequestTimeout = RequestTimeout,
        KeepAliveInterval = KeepAliveInterval,
        TeardownTimeout = TeardownTimeout,
        ReceiveBufferSize = ReceiveBufferSize,
    };
}
