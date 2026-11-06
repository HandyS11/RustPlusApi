namespace RustPlusApi.Fcm.Registration.Steps.Browser;

/// <summary>A Chromium-family browser that can be launched under remote control.</summary>
/// <param name="Name">Display name, e.g. <c>Microsoft Edge</c>.</param>
/// <param name="ExecutablePath">Absolute path of the program to start (the <c>flatpak</c> binary for a Flatpak app).</param>
/// <param name="PrefixArguments">Arguments that precede the browser's own switches, e.g. <c>run com.google.Chrome</c>.</param>
/// <param name="FlatpakAppId">The Flatpak application id, or <see langword="null"/> when not a Flatpak app.</param>
internal sealed record BrowserInfo(
    string Name,
    string ExecutablePath,
    IReadOnlyList<string> PrefixArguments,
    string? FlatpakAppId);

/// <summary>The outcome of a browser search.</summary>
/// <param name="Browser">The browser found, or <see langword="null"/> when none usable exists.</param>
/// <param name="SkippedSnapPaths">Snap-packaged browsers, and launcher scripts that start one, that were found but skipped, because Snap confinement blocks the debugging port.</param>
internal sealed record BrowserSearchResult(BrowserInfo? Browser, IReadOnlyList<string> SkippedSnapPaths);
