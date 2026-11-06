using System.Diagnostics;
using System.Text;

namespace RustPlusApi.Fcm.Registration.Steps.Browser;

/// <summary>Builds the command line that starts a browser with remote debugging. <c>ProcessStartInfo.ArgumentList</c>
/// does not exist on netstandard2.0, so the arguments are quoted into one <c>Arguments</c> string.</summary>
internal static class BrowserLaunchArguments
{
    private const string ProfileFolderName = "RustPlusApi.SteamLogin";

    /// <summary>Quotes one argument following the <c>CommandLineToArgvW</c> rules (also how .NET parses on Unix).</summary>
    /// <param name="value">The raw argument.</param>
    internal static string Quote(string value)
    {
        if (value.Length > 0 && value.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            return value;
        }

        var builder = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in value)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                builder.Append('\\', (backslashes * 2) + 1);
            }
            else
            {
                builder.Append('\\', backslashes);
            }

            backslashes = 0;
            builder.Append(c);
        }

        builder.Append('\\', backslashes * 2);
        return builder.Append('"').ToString();
    }

    /// <summary>Builds the start info for launching <paramref name="browser"/> with a debugging port and a throwaway profile.</summary>
    /// <param name="browser">The browser to launch.</param>
    /// <param name="port">The remote-debugging port.</param>
    /// <param name="profileDirectory">The user-data directory.</param>
    internal static ProcessStartInfo BuildStartInfo(BrowserInfo browser, int port, string profileDirectory)
    {
        var arguments = new List<string>();
        foreach (var prefix in browser.PrefixArguments)
        {
            arguments.Add(Quote(prefix));
        }

        arguments.Add($"--remote-debugging-port={port}");
        arguments.Add(Quote($"--user-data-dir={profileDirectory}"));
        arguments.Add("--no-first-run");
        arguments.Add("--no-default-browser-check");
        arguments.Add("--new-window");
        arguments.Add("about:blank");

        return new ProcessStartInfo
        {
            FileName = browser.ExecutablePath,
            Arguments = string.Join(" ", arguments),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
    }

    /// <summary>
    /// The directory under which per-login profiles are created. It must be private to the user, because a profile
    /// holds Steam and Facepunch sessions:
    /// <list type="bullet">
    /// <item>A Flatpak app can only write inside its own sandbox: <c>~/.var/app/&lt;app-id&gt;/cache</c>.</item>
    /// <item>On Windows and macOS the temporary directory is already per-user.</item>
    /// <item>On Linux <c>/tmp</c> is shared between local users, and netstandard2.0 cannot restrict a directory's
    /// permissions, so the user's cache directory is used: <c>$XDG_CACHE_HOME</c> when it is an absolute path (the XDG
    /// Base Directory spec says to ignore a relative one), else <c>~/.cache</c>.</item>
    /// </list>
    /// </summary>
    /// <param name="browser">The browser to be launched.</param>
    /// <param name="environment">The operating-system seam.</param>
    internal static string GetProfileRoot(BrowserInfo browser, IBrowserEnvironment environment)
    {
        if (browser.FlatpakAppId is not null)
        {
            return Path.Combine(environment.HomeDirectory, ".var", "app", browser.FlatpakAppId, "cache",
                ProfileFolderName);
        }

        if (environment.IsWindows || environment.IsMacOS)
        {
            return Path.Combine(environment.TempDirectory, ProfileFolderName);
        }

        var xdgCacheHome = environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var cache = xdgCacheHome is not null && Path.IsPathRooted(xdgCacheHome)
            ? xdgCacheHome
            : Path.Combine(environment.HomeDirectory, ".cache");
        return Path.Combine(cache, ProfileFolderName);
    }
}
