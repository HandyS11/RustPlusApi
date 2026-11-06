using System.Text.RegularExpressions;

namespace RustPlusApi.Fcm.Registration.Steps.Browser;

/// <summary>
/// Rewrites a browser user agent so it no longer names Windows. Facepunch answers "Login Failed" to
/// a Windows browser on the Rust+ login page, so the controlled browser presents itself as Linux.
/// </summary>
internal static class UserAgentRewriter
{
    /// <summary>The <c>Sec-CH-UA-Platform</c> client-hint value that matches the rewritten user agent.</summary>
    internal const string NonWindowsPlatformHint = "\"Linux\"";

    private const string NonWindowsPlatform = "(X11; Linux x86_64)";

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex WindowsGroup = new(
        @"\([^)]*windows[^)]*\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);

    private static readonly Regex WindowsWord = new(
        "windows", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);

    /// <summary>Whether <paramref name="userAgent"/> contains <c>windows</c>, case-insensitively.</summary>
    /// <param name="userAgent">The user agent to inspect; <see langword="null"/> and empty say nothing.</param>
    internal static bool SaysWindows(string? userAgent) =>
        !string.IsNullOrEmpty(userAgent) && WindowsWord.IsMatch(userAgent);

    /// <summary>Replaces the first parenthesised group naming Windows with a Linux platform group, then
    /// removes any remaining <c>windows</c> text.</summary>
    /// <param name="userAgent">The user agent to rewrite.</param>
    internal static string ToNonWindows(string userAgent)
    {
        var replaced = WindowsGroup.Replace(userAgent, NonWindowsPlatform, 1);
        return WindowsWord.Replace(replaced, string.Empty);
    }
}
