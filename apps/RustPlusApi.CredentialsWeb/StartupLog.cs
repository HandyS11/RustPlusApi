namespace RustPlusApi.CredentialsWeb;

/// <summary>Source-generated, structured log messages emitted while the host starts. Generated
/// bodies carry <c>[GeneratedCode]</c> and are excluded from the coverage gate automatically.</summary>
internal static partial class StartupLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "CredentialsWeb:{Setting} is set but is no longer read. The Steam return URL is "
                  + "now derived from the request, so this setting has no effect and can be removed.")]
    public static partial void LogRetiredSetting(this ILogger logger, string setting);

    /// <summary>Emitted on every start with no proxy configured, which is also the correct state for
    /// the ordinary loopback <c>docker run</c>. The app cannot tell that instance from a proxied one,
    /// so this is Information rather than Warning — a warning on the documented happy path is noise —
    /// and it is phrased as a conditional aimed at the operator who is actually behind a proxy.</summary>
    [LoggerMessage(Level = LogLevel.Information,
        Message = "CredentialsWeb:KnownProxies is empty, so forwarded headers are not trusted and "
                  + "every visitor is accounted by the address this process sees. That is correct "
                  + "for a loopback or LAN instance. If this instance is behind a reverse proxy, "
                  + "every visitor is accounted as the proxy instead: they share one per-IP bucket, "
                  + "the hourly completion cap becomes a global ceiling, and they evict each other's "
                  + "sessions mid-login. Set CredentialsWeb__KnownProxies__0 to the proxy's address.")]
    public static partial void LogNoKnownProxies(this ILogger logger);
}
