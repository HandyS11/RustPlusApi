using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using RustPlusApi.CredentialsWeb;
using RustPlusApi.CredentialsWeb.Endpoints;
using RustPlusApi.CredentialsWeb.Flow;
using RustPlusApi.CredentialsWeb.Security;
using RustPlusApi.CredentialsWeb.Sessions;
using RustPlusApi.CredentialsWeb.Upstream;
using System.Diagnostics.CodeAnalysis;

var builder = WebApplication.CreateBuilder(args);

// ASP.NET Core's hosting diagnostics log the full request line — path AND query — at Information.
// The Facepunch callback carries the Steam auth token in its query string, so that logger is
// silenced outright rather than filtered per-path: the "Request starting" entry is written before
// any middleware of ours could redact it. Enforced by SecretsAreNeverLoggedTests.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);

// Microsoft.AspNetCore.HostFiltering logs the request path at Information level when host validation
// runs. A request body containing a pasted callback URL should not appear in logs here either.
builder.Logging.AddFilter("Microsoft.AspNetCore.HostFiltering", LogLevel.Warning);

// Various ASP.NET Core routing and MVC loggers can emit request paths at Information level.
// These should not log the /api/callback path.
builder.Logging.AddFilter("Microsoft.AspNetCore.Routing", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Mvc", LogLevel.Warning);

var options = new AppOptions();

// Every AppOptions member is internal (this app's blanket visibility rule), so the default binder
// — which only reflects over public properties — would silently leave everything at its default.
// BindNonPublicProperties opts into binding those internal setters instead.
builder.Configuration.GetSection(AppOptions.SectionName)
    .Bind(options, static o => o.BindNonPublicProperties = true);

var validationError = AppOptionsValidator.Validate(options);
if (validationError is not null)
{
    await Console.Error.WriteLineAsync($"Configuration error: {validationError}").ConfigureAwait(false);
    return 1;
}

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddSingleton<CredentialFlow>();
builder.Services.AddHostedService<SessionSweeper>();
builder.Services.AddHttpClient(LiveRegistrationSteps.HttpClientName);
builder.Services.AddSingleton<IRegistrationSteps>(serviceProvider => new LiveRegistrationSteps(
    serviceProvider.GetRequiredService<IHttpClientFactory>(),
    serviceProvider.GetRequiredService<ILoggerFactory>()));

// Vestigial: the app is loopback-only (Facepunch only redirects to a loopback returnUrl), so no
// reverse proxy can front the Steam login and this should never engage. Retained rather than
// removed so the caps keep behaving exactly as their tests describe. See the app README.
// Without this, every visitor behind a reverse proxy presents as the proxy and shares one per-IP
// bucket, silently voiding the caps. Configured too loosely it is worse: trusting X-Forwarded-For
// from anyone lets a caller spoof their way past the limits. So the operator must name their proxy
// explicitly, and with none named the headers are ignored.
builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
{
    forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in options.KnownProxies)
    {
        forwarded.KnownProxies.Add(IPAddress.Parse(proxy));
    }
});

var app = builder.Build();

// These two were required until the return URL started coming from the request. An instance that
// still sets them keeps working, so an existing `docker run` does not break — it is just told.
string[] retiredSettings = ["PublicBaseUrl", "AllowInsecureBaseUrl"];
foreach (var setting in retiredSettings.Where(setting =>
             builder.Configuration[$"{AppOptions.SectionName}:{setting}"] is not null))
{
    app.Logger.LogRetiredSetting(setting);
}

// ForwardedHeadersOptions ships with its own default trusted network (loopback), so merely leaving
// KnownProxies unpopulated does NOT make the middleware ignore X-Forwarded-For — it would still
// honor it from what it considers the loopback proxy. And the documented way to stop trusting that
// default — clearing KnownNetworks/KnownProxies — means the opposite of what it sounds like: an
// empty restriction list is treated as "trust every address," not "trust none." The only way to
// genuinely ignore forwarded headers with nothing configured is to never run this middleware at all.
if (options.KnownProxies.Count > 0)
{
    app.UseForwardedHeaders();
}

app.UseSecurityHeaders();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapSessionEndpoints();
app.MapCallbackEndpoints();
app.MapEventEndpoints();

await app.RunAsync().ConfigureAwait(false);
return 0;

/// <summary>Entry point marker so <c>WebApplicationFactory&lt;Program&gt;</c> can boot the app in tests.
/// No accessibility modifier: since .NET 10 the compiler already emits the top-level-statement
/// <c>Program</c> class as public, so restating it here is what ASP0027 flags. This part only
/// carries the attributes below.</summary>
[ExcludeFromCodeCoverage(Justification = "Host wiring: composition only, exercised end to end by the endpoint tests.")]
[SuppressMessage("Major Code Smell", "S1118:Utility classes should not have public constructors",
    Justification =
        "Not a utility class — an empty marker type WebApplicationFactory<Program> uses to locate the app's assembly.")]
partial class Program;
