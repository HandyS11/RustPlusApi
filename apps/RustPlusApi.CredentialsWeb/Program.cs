using RustPlusApi.CredentialsWeb;
using System.Diagnostics.CodeAnalysis;

var builder = WebApplication.CreateBuilder(args);

// ASP.NET Core's hosting diagnostics log the full request line — path AND query — at Information.
// The Facepunch callback carries the Steam auth token in its query string, so that logger is
// silenced outright rather than filtered per-path: the "Request starting" entry is written before
// any middleware of ours could redact it. Enforced by SecretsAreNeverLoggedTests.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);

var options = new AppOptions();
builder.Configuration.GetSection(AppOptions.SectionName).Bind(options);

var validationError = AppOptionsValidator.Validate(options);
if (validationError is not null)
{
    await Console.Error.WriteLineAsync($"Configuration error: {validationError}").ConfigureAwait(false);
    return 1;
}

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

await app.RunAsync().ConfigureAwait(false);
return 0;

/// <summary>Entry point marker so <c>WebApplicationFactory&lt;Program&gt;</c> can boot the app in tests.</summary>
[ExcludeFromCodeCoverage(Justification = "Host wiring: composition only, exercised end to end by the endpoint tests.")]
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Must stay public: WebApplicationFactory<Program> in the test assembly needs a public type parameter.")]
[SuppressMessage("Major Code Smell", "S1118:Utility classes should not have public constructors",
    Justification = "Not a utility class — an empty marker type WebApplicationFactory<Program> uses to locate the app's assembly.")]
public partial class Program;
