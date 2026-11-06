using System.Diagnostics.CodeAnalysis;

namespace RustPlusApi.Fcm.Registration.Steps.Browser;

/// <summary>Finds an installed Chromium-family browser: an explicit path, else the platform's well-known locations.</summary>
/// <param name="environment">The operating-system seam.</param>
internal sealed class BrowserLocator(IBrowserEnvironment environment)
{
    private const char WindowsSeparator = '\\';

    /// <summary>How much of a candidate is read to recognise a launcher script; such scripts are a few hundred bytes.</summary>
    private const int ScriptHeadBytes = 4096;

    private static readonly string[] LinuxExecutableNames =
    [
        "google-chrome", "google-chrome-stable", "chromium", "chromium-browser",
        "microsoft-edge", "microsoft-edge-stable", "brave-browser",
    ];

    private static readonly (string Name, string AppId)[] FlatpakApps =
    [
        ("Google Chrome", "com.google.Chrome"),
        ("Microsoft Edge", "com.microsoft.Edge"),
        ("Brave", "com.brave.Browser"),
        ("Chromium", "org.chromium.Chromium"),
    ];

    private static readonly string[] MacApps = ["Google Chrome", "Microsoft Edge", "Brave Browser", "Chromium"];

    /// <summary>Finds a browser.</summary>
    /// <param name="explicitPath">A caller-chosen executable, used as is when set.</param>
    /// <exception cref="InvalidOperationException"><paramref name="explicitPath"/> is set but does not exist.</exception>
    internal BrowserSearchResult Find(string? explicitPath)
    {
        if (!string.IsNullOrEmpty(explicitPath))
        {
            if (!environment.FileExists(explicitPath!))
            {
                throw new InvalidOperationException($"The browser executable '{explicitPath}' does not exist.");
            }

            return Result(new BrowserInfo(Path.GetFileNameWithoutExtension(explicitPath), explicitPath!, [], null));
        }

        if (environment.IsWindows)
        {
            return Result(FindOnWindows());
        }

        if (environment.IsMacOS)
        {
            return Result(FindOnMac());
        }

        return FindOnLinux();
    }

    private static BrowserSearchResult Result(BrowserInfo? browser) => new(browser, []);

    /// <summary>Whether a path lies in a Snap install root: <c>/snap</c>, or <c>/var/lib/snapd/snap</c> where
    /// snapd lives on Fedora and Arch.</summary>
    /// <param name="path">The candidate's path.</param>
    private static bool IsSnap(string path) =>
        path.StartsWith("/snap/", StringComparison.Ordinal)
        || path.StartsWith("/var/lib/snapd/snap/", StringComparison.Ordinal);

    /// <summary>Whether a candidate is a launcher script that starts a Snap, such as the <c>/usr/bin/chromium-browser</c>
    /// that Ubuntu's transitional apt package installs (<c>exec /snap/bin/chromium "$@"</c>).</summary>
    /// <param name="path">The candidate's path.</param>
    private bool IsSnapLauncherScript(string path)
    {
        var head = environment.ReadFileHead(path, ScriptHeadBytes);
#pragma warning disable CA2249 // string.Contains(string, StringComparison) is not available on netstandard2.0
        return head?.StartsWith("#!", StringComparison.Ordinal) == true
               && head.IndexOf("/snap/", StringComparison.Ordinal) >= 0;
#pragma warning restore CA2249
    }

    private BrowserInfo? FindOnWindows()
    {
        var pf86 = environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var pf = environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var local = environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        const string edge = @"Microsoft\Edge\Application\msedge.exe";
        const string chrome = @"Google\Chrome\Application\chrome.exe";
        const string brave = @"BraveSoftware\Brave-Browser\Application\brave.exe";

        (string Name, string Root, string Relative)[] candidates =
        [
            ("Microsoft Edge", pf86, edge),
            ("Microsoft Edge", pf, edge),
            ("Google Chrome", pf, chrome),
            ("Google Chrome", pf86, chrome),
            ("Google Chrome", local, chrome),
            ("Brave", pf, brave),
            ("Brave", local, brave),
        ];

        foreach (var (name, root, relative) in candidates)
        {
            if (string.IsNullOrEmpty(root))
            {
                continue;
            }

            var path = root + WindowsSeparator + relative;
            if (environment.FileExists(path))
            {
                return new BrowserInfo(name, path, [], null);
            }
        }

        return null;
    }

    [SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded",
        Justification = "macOS installs application bundles at a fixed location.")]
    private BrowserInfo? FindOnMac()
    {
        foreach (var app in MacApps)
        {
            var path = $"/Applications/{app}.app/Contents/MacOS/{app}";
            if (environment.FileExists(path))
            {
                return new BrowserInfo(app, path, [], null);
            }
        }

        return null;
    }

    private BrowserSearchResult FindOnLinux()
    {
        // Relative entries ('.', 'bin', an unexpanded '~/bin') resolve against the current directory, where a planted
        // look-alike could sit. The browser and flatpak are started by the path found here, so it must be absolute.
        var directories = (environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split([Path.PathSeparator], StringSplitOptions.RemoveEmptyEntries)
            .Where(Path.IsPathRooted)
            .ToArray();
        var skipped = new List<string>();
        BrowserInfo? found = null;

        // Scan every candidate, even after a winner is known, so every skipped Snap browser is reported.
        foreach (var executable in LinuxExecutableNames)
        {
            foreach (var directory in directories)
            {
                var path = Path.Combine(directory, executable);
                if (!environment.FileExists(path))
                {
                    continue;
                }

                if (IsSnap(path) || IsSnapLauncherScript(path))
                {
                    skipped.Add(path);
                }
                else
                {
                    found ??= new BrowserInfo(executable, path, [], null);
                }
            }
        }

        return new BrowserSearchResult(found ?? FindFlatpak(directories), skipped);
    }

    [SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded",
        Justification = "Flatpak's system-wide installation root is fixed by the Flatpak specification.")]
    private BrowserInfo? FindFlatpak(string[] pathDirectories)
    {
        string? flatpak = null;
        foreach (var directory in pathDirectories)
        {
            var candidate = Path.Combine(directory, "flatpak");
            if (environment.FileExists(candidate))
            {
                flatpak = candidate;
                break;
            }
        }

        if (flatpak is null)
        {
            return null;
        }

        var userApps = Path.Combine(environment.HomeDirectory, ".local", "share", "flatpak", "app");
        foreach (var (name, appId) in FlatpakApps)
        {
            if (environment.DirectoryExists(Path.Combine("/var/lib/flatpak/app", appId))
                || environment.DirectoryExists(Path.Combine(userApps, appId)))
            {
                return new BrowserInfo(name, flatpak, ["run", appId], appId);
            }
        }

        return null;
    }
}
