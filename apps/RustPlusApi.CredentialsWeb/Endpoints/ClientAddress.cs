namespace RustPlusApi.CredentialsWeb.Endpoints;

/// <summary>Resolves the caller's address for per-IP accounting.</summary>
internal static class ClientAddress
{
    /// <summary>The remote address, already rewritten by the forwarded-headers middleware when the
    /// instance is configured with known proxies. Falls back to a constant so accounting still
    /// happens (conservatively, as one shared bucket) rather than silently disappearing.</summary>
    /// <param name="context">The current request.</param>
    internal static string Of(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
