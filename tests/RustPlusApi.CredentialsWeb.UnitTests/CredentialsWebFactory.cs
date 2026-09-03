using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using RustPlusApi.CredentialsWeb.Upstream;

namespace RustPlusApi.CredentialsWeb.UnitTests;

/// <summary>Boots the real app with the upstream seam and the clock replaced.</summary>
internal sealed class CredentialsWebFactory : WebApplicationFactory<Program>
{
    internal const string BaseUrl = "https://creds.example.org";

    private static readonly DateTimeOffset Origin = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    internal CredentialsWebFactory(IDictionary<string, string>? settings = null)
    {
        // Program.cs binds configuration before builder.Build(), which is earlier than any
        // WebApplicationFactory configuration hook runs — so these must be environment variables.
        SetEnvironment("CredentialsWeb__PublicBaseUrl", BaseUrl);
        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            SetEnvironment(key, value);
        }
    }

    internal FakeRegistrationSteps Steps { get; } = new();

    internal FakeTimeProvider Time { get; } = new(Origin);

    private List<string> EnvironmentKeys { get; } = [];

    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRegistrationSteps>();
            services.AddSingleton<IRegistrationSteps>(Steps);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        });

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var key in EnvironmentKeys)
            {
                Environment.SetEnvironmentVariable(key, null);
            }
        }

        base.Dispose(disposing);
    }

    private void SetEnvironment(string key, string value)
    {
        Environment.SetEnvironmentVariable(key, value);
        EnvironmentKeys.Add(key);
    }
}
