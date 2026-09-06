using System.Net;

namespace RustPlusApi.CredentialsWeb;

/// <summary>Every knob the app exposes. Bound from the "CredentialsWeb" configuration section.</summary>
internal sealed class AppOptions
{
    /// <summary>Configuration section these options bind from.</summary>
    internal const string SectionName = "CredentialsWeb";

    /// <summary>The loopback origin the browser opens, with no trailing slash. Required, and it
    /// must be a loopback one: Facepunch only honours the returnUrl redirect for loopback, so a
    /// routable value produces a login that never calls back. See the app README.</summary>
    internal string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>Permits a non-https <see cref="PublicBaseUrl"/>. Left over from the abandoned hosted
    /// design: on the loopback-only deployment this app actually supports, plain http is normal.</summary>
    internal bool AllowInsecureBaseUrl { get; set; }

    /// <summary>Addresses of reverse proxies whose <c>X-Forwarded-For</c> is trusted. Empty means
    /// forwarded headers are ignored, which is the safe default: trusting them from anyone lets a
    /// caller spoof their address past every per-IP cap. Vestigial — no reverse proxy can front the
    /// Steam login — but retained rather than silently changing behaviour.</summary>
    internal IList<string> KnownProxies { get; } = [];

    /// <summary>Global cap on live sessions in any state.</summary>
    internal int MaxConcurrentSessions { get; set; } = 200;

    /// <summary>Global cap on concurrent MCS sockets — the genuinely scarce resource.</summary>
    internal int MaxConcurrentPairings { get; set; } = 50;

    /// <summary>Per-IP cap on completed flows in a rolling hour. Bounds Google device registrations.</summary>
    internal int MaxCompletionsPerIpPerHour { get; set; } = 5;

    /// <summary>Lifetime of a session that has not yet completed the Steam login. Shortest leash:
    /// this is the cheapest state to create and therefore the cheapest to spam.</summary>
    internal TimeSpan CreatedTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Lifetime of a session once the Steam login has completed.</summary>
    internal TimeSpan SessionTtl { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Maximum time an MCS socket is held waiting for a pairing push.</summary>
    internal TimeSpan PairingTtl { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>Pure validation for <see cref="AppOptions"/>, kept separate from host wiring so it is
/// unit-testable without building a web host.</summary>
internal static class AppOptionsValidator
{
    /// <summary>Returns <see langword="null"/> when the options are usable, or a message naming the
    /// offending setting.</summary>
    /// <param name="options">The options to validate.</param>
    internal static string? Validate(AppOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.PublicBaseUrl))
        {
            return $"{AppOptions.SectionName}:PublicBaseUrl is required. Set it to the loopback "
                   + "origin you open in the browser, for example http://localhost:8080.";
        }

        if (!Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var baseUri))
        {
            return $"{AppOptions.SectionName}:PublicBaseUrl must be an absolute URL, "
                   + $"but was '{options.PublicBaseUrl}'.";
        }

        if (options.PublicBaseUrl.EndsWith('/'))
        {
            return $"{AppOptions.SectionName}:PublicBaseUrl must not have a trailing slash.";
        }

        if (!baseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.Ordinal)
            && !options.AllowInsecureBaseUrl)
        {
            return $"{AppOptions.SectionName}:PublicBaseUrl must use https, because it carries the "
                   + "Steam auth token back from Facepunch. For the usual http://localhost setup, set "
                   + $"{AppOptions.SectionName}:AllowInsecureBaseUrl=true.";
        }

        foreach (var proxy in options.KnownProxies)
        {
            if (!IPAddress.TryParse(proxy, out _))
            {
                return $"{AppOptions.SectionName}:KnownProxies contains '{proxy}', which is not an "
                       + "IP address.";
            }
        }

        if (options.MaxConcurrentSessions <= 0)
        {
            return $"{AppOptions.SectionName}:MaxConcurrentSessions must be greater than zero.";
        }

        if (options.MaxConcurrentPairings <= 0)
        {
            return $"{AppOptions.SectionName}:MaxConcurrentPairings must be greater than zero.";
        }

        if (options.MaxCompletionsPerIpPerHour <= 0)
        {
            return $"{AppOptions.SectionName}:MaxCompletionsPerIpPerHour must be greater than zero.";
        }

        if (options.CreatedTtl <= TimeSpan.Zero)
        {
            return $"{AppOptions.SectionName}:CreatedTtl must be greater than zero.";
        }

        if (options.SessionTtl <= TimeSpan.Zero)
        {
            return $"{AppOptions.SectionName}:SessionTtl must be greater than zero.";
        }

        return options.PairingTtl <= TimeSpan.Zero
            ? $"{AppOptions.SectionName}:PairingTtl must be greater than zero."
            : null;
    }
}
