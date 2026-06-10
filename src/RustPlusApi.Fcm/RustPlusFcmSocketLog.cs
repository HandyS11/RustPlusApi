using Microsoft.Extensions.Logging;
using static RustPlusApi.Fcm.Data.Tags;

namespace RustPlusApi.Fcm;

/// <summary>Source-generated, structured log messages for <see cref="RustPlusFcmSocket"/> and
/// <see cref="RustPlusFcm"/>. Generated bodies carry <c>[GeneratedCode]</c> and are excluded from
/// the coverage gate automatically.</summary>
internal static partial class RustPlusFcmSocketLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Exception occurred on ConnectAsync.")]
    public static partial void LogConnectFailed(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Background loop faulted during teardown (expected).")]
    public static partial void LogTeardownLoopFaulted(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Responding to ping: StreamId={StreamId}, Last={Last}, Status={Status}")]
    public static partial void LogRespondingToPing(this ILogger logger, int? streamId, int? last, long? status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring unrecognized tag: {Tag}")]
    public static partial void LogUnrecognizedTag(this ILogger logger, McsProtoTag tag);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No AppData found in message.")]
    public static partial void LogNoAppData(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Not a Rust+ notification - missing channelId or body.")]
    public static partial void LogNotRustNotification(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unknown channel: {ChannelId}")]
    public static partial void LogUnknownChannel(this ILogger logger, string channelId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unknown pairing type: {Type}")]
    public static partial void LogUnknownPairingType(this ILogger logger, string type);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unknown entity type: {EntityType}")]
    public static partial void LogUnknownEntityType(this ILogger logger, int? entityType);
}
