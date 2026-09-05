namespace RustPlusApi.CredentialsWeb.Security;

/// <summary>Response headers that back the page's trust claims: no referrer leakage, no caching of
/// anything that carries a credential, and a content policy admitting no third-party origin — which
/// is also what keeps the client a single auditable file.</summary>
internal static class SecurityHeaders
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; "
        + "connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    /// <summary>Adds the headers to every response.</summary>
    /// <param name="app">The application pipeline.</param>
    internal static void UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.ContentSecurityPolicy = ContentSecurityPolicy;
                headers["Referrer-Policy"] = "no-referrer";
                headers.XContentTypeOptions = "nosniff";
                headers.CacheControl = "no-store";
                return Task.CompletedTask;
            });

            await next(context).ConfigureAwait(false);
        });
}
