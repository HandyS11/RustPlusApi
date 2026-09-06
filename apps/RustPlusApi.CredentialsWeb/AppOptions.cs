using System.Net;

namespace RustPlusApi.CredentialsWeb;

/// <summary>Every knob the app exposes. Bound from the "CredentialsWeb" configuration section.</summary>
internal sealed class AppOptions
{
    /// <summary>Configuration section these options bind from.</summary>
    internal const string SectionName = "CredentialsWeb";

    /// <summary>Allows the pairing wait for a visitor who did not reach the app over loopback. Off by
    /// default: the wait holds an MCS socket per visitor, which is the one genuinely scarce resource
    /// here, and a public instance has no reason to hold one for a stranger. Turn it on when
    /// self-hosting on an address that is yours but is not loopback, such as a LAN address.</summary>
    internal bool AllowRemotePairing { get; set; }

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

    /// <summary>Lifetime of a session that has not yet completed the Steam login. It has to cover a
    /// real Steam login, two-factor included, and on a hosted instance a copy and a paste as well.
    /// Still the shortest leash of the three: this is the cheapest state to create and so the
    /// cheapest to spam.</summary>
    internal TimeSpan CreatedTtl { get; set; } = TimeSpan.FromMinutes(10);

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
