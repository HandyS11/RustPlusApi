namespace RustPlusApi.CredentialsWeb.Flow;

/// <summary>Source-generated, structured log messages for <see cref="CredentialFlow"/>. Generated
/// bodies carry <c>[GeneratedCode]</c> and are excluded from the coverage gate automatically.</summary>
internal static partial class CredentialFlowLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Credential flow failed during {Step} for session {SessionId}.")]
    public static partial void LogFlowFailed(this ILogger logger, string step, string sessionId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Pairing wait failed for session {SessionId}.")]
    public static partial void LogPairingFailed(this ILogger logger, string sessionId, Exception exception);
}
