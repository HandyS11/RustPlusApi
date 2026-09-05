using Microsoft.Extensions.Logging;

namespace RustPlusApi.CredentialsWeb.Sessions;

/// <summary>Source-generated, structured log messages for <see cref="SessionSweeper"/>. Generated
/// bodies carry <c>[GeneratedCode]</c> and are excluded from the coverage gate automatically.</summary>
internal static partial class SessionSweeperLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Sweep failed; the next tick will retry.")]
    public static partial void LogSweepFailed(this ILogger logger, Exception exception);
}
