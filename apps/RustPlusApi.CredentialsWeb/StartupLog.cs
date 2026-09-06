namespace RustPlusApi.CredentialsWeb;

/// <summary>Source-generated, structured log messages emitted while the host starts. Generated
/// bodies carry <c>[GeneratedCode]</c> and are excluded from the coverage gate automatically.</summary>
internal static partial class StartupLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "CredentialsWeb:{Setting} is set but is no longer read. The Steam return URL is "
                  + "now derived from the request, so this setting has no effect and can be removed.")]
    public static partial void LogRetiredSetting(this ILogger logger, string setting);
}
