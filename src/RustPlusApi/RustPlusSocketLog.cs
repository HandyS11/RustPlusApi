using Microsoft.Extensions.Logging;

namespace RustPlusApi;

/// <summary>Source-generated, structured log messages for <see cref="RustPlusSocket"/> and
/// <see cref="RustPlus"/>. Generated bodies carry <c>[GeneratedCode]</c> and are excluded from the
/// coverage gate automatically.</summary>
internal static partial class RustPlusSocketLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Receiving data from the Rust+ server.")]
    public static partial void LogReceivingData(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Waiting for data.")]
    public static partial void LogWaitingForData(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Receive loop exited.")]
    public static partial void LogReceiveLoopExited(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Received message: {Message}")]
    public static partial void LogReceivedMessage(this ILogger logger, object message);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Received notification: {Message}")]
    public static partial void LogReceivedNotification(this ILogger logger, object message);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Received response: {Message}")]
    public static partial void LogReceivedResponse(this ILogger logger, object message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unknown broadcast received: {Broadcast}")]
    public static partial void LogUnknownBroadcast(this ILogger logger, object broadcast);

    [LoggerMessage(Level = LogLevel.Error, Message = "Exception occurred on ConnectAsync.")]
    public static partial void LogConnectFailed(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Previous receive loop faulted before reconnect (expected).")]
    public static partial void LogPreviousReceiveLoopFaulted(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Background loop faulted during teardown (expected).")]
    public static partial void LogTeardownLoopFaulted(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Send loop stopped due to a WebSocketException.")]
    public static partial void LogSendLoopFaulted(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Disconnected from the Rust+ socket due to a WebSocketException.")]
    public static partial void LogReceiveWebSocketFault(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Receive loop encountered a non-fatal exception; backing off before retrying.")]
    public static partial void LogReceiveFault(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Broadcast-reply matcher threw; treating as no match.")]
    public static partial void LogMatcherThrew(this ILogger logger, Exception exception);
}
