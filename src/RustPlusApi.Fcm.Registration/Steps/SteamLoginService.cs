using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using RustPlusApi.Fcm.Registration.Steps.Browser;
using RustPlusApi.Fcm.Registration.Steps.Cdp;

namespace RustPlusApi.Fcm.Registration.Steps;

/// <summary>
/// Step 5 — interactive Steam login. Opens a Chromium-family browser window the library controls (Edge on Windows,
/// otherwise Chrome, Brave or Chromium, including Flatpak) on Facepunch's Rust+ app login, and reads the
/// <c>steamId</c> and <c>token</c> from Facepunch's final <c>rust-plus://rust-auth-session</c> redirect over the
/// Chrome DevTools Protocol.
/// </summary>
/// <param name="port">Kept so existing callers keep compiling; no longer used. The browser's debugging port is picked
/// automatically on every login.</param>
/// <remarks>
/// <para>The browser runs with a fresh, temporary profile, so the user signs in to Steam in that window even if they are
/// signed in elsewhere. The profile is deleted when the login ends, whichever way it ends; if deletion still fails
/// after retries (a browser helper process can hold files briefly) the login still succeeds and the next login removes
/// the leftover. If the host process is killed hard (debugger stop, <c>kill</c>), the browser window may stay open
/// until the user closes it.</para>
/// <para>When no usable browser is found (none installed, Snap only, a headless machine), its temporary profile cannot
/// be created, or it fails to start, the login falls back to asking for the address by hand through
/// <see cref="SteamLoginOptions.ReadPastedAddress"/>.</para>
/// <para>The browser picks its own debugging port, binding it as it starts, and the library reads it from the
/// <c>DevToolsActivePort</c> file in the private profile directory, so no other process can take the port first. The
/// port listens on the loopback address without authentication, so any local process could still attach to it while
/// the login runs; that is accepted as a local risk.</para>
/// </remarks>
#pragma warning disable CS9113 // 'port' is kept for source compatibility with existing callers; the login no longer listens on a port.
public sealed class SteamLoginService(int port = 3000)
#pragma warning restore CS9113
{
    /// <summary>Instructions for obtaining the Rust+ login redirect by hand, for hosts that cannot
    /// capture it automatically.</summary>
    public const string PasteInstructions =
        "Open the login address in any browser and sign in to Steam. Then open the browser's developer tools (F12), "
        + "go to the Network panel, and find the last request whose name starts with app?returnUrl=rust-plus. "
        + "Copy the value of its Location response header: it starts with rust-plus://rust-auth-session. "
        + "On Windows, first switch the developer tools to a non-Windows user agent, otherwise Facepunch answers \"Login Failed\".";

    private const string BrowserRejectedMessage = "The browser rejected a debugging command during the Steam login.";

    private const string NotTheLoginAddressMessage =
        "That is not the Rust+ login address. Paste the address that starts with "
        + RegistrationConstants.RustPlusAuthRedirectPrefix + ".";

    private const int ProfileDeleteAttempts = 20;
    private static readonly TimeSpan ProfileDeleteDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan BrowserCloseTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Gets the optional inputs: a paste callback for when no browser can be driven, and an explicit browser path.</summary>
    public SteamLoginOptions Options { get; init; } = new();

    /// <summary>Runs the Steam login in a browser window the library opens, and returns the captured identity.</summary>
    /// <param name="onLoginUrl">Invoked with the login URL first, before any browser is looked for, so callers
    /// can print it. Always invoked.</param>
    /// <param name="cancellationToken">Token to cancel the operation. The browser is still closed and its profile
    /// deleted.</param>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the user closes the browser window before the login
    /// finishes, Facepunch refuses the login, the browser rejects a debugging command, the redirect carries no usable
    /// <c>token</c> or <c>steamId</c>, <see cref="SteamLoginOptions.BrowserPath"/> does not exist, or no browser can be
    /// driven and <see cref="SteamLoginOptions.ReadPastedAddress"/> is not set (the message then explains how to get
    /// the address by hand). Messages never contain the token.</exception>
    /// <remarks>Excluded from coverage: a one-line composition root that wires in the real operating system and the
    /// real browser launcher, so calling it starts a browser. The orchestration it delegates to is unit-tested over
    /// fakes.</remarks>
    [ExcludeFromCodeCoverage]
    public Task<SteamLoginResult> LoginAsync(Action<string>? onLoginUrl = null,
        CancellationToken cancellationToken = default) =>
        LoginAsync(onLoginUrl, SystemBrowserEnvironment.Instance, new ChromiumLauncher(), cancellationToken);

    /// <summary>Runs the login over the given operating-system and launcher seams.</summary>
    /// <param name="onLoginUrl">Invoked with the login URL first.</param>
    /// <param name="environment">The operating system, for the browser lookup and the profile location.</param>
    /// <param name="launcher">Starts the browser.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    /// <exception cref="InvalidOperationException">See the public overload.</exception>
    internal async Task<SteamLoginResult> LoginAsync(Action<string>? onLoginUrl,
        IBrowserEnvironment environment,
        IBrowserLauncher launcher,
        CancellationToken cancellationToken)
    {
        var loginUrl = BuildLoginUrl(RegistrationConstants.RustPlusAppReturnUrl);
        onLoginUrl?.Invoke(loginUrl);

        var search = new BrowserLocator(environment).Find(Options.BrowserPath);
        if (search.Browser is not { } browser)
        {
            var reason = search.SkippedSnapPaths.Count > 0
                ? $"Snap browsers are not supported ({string.Join(", ", search.SkippedSnapPaths)})."
                : "No Chrome, Edge, Brave or Chromium browser was found.";
            return await ReadPastedAddressAsync(reason, loginUrl, cancellationToken).ConfigureAwait(false);
        }

        // A cancelled login must not flash a browser window: the launcher would only notice once the process runs.
        cancellationToken.ThrowIfCancellationRequested();
        var root = BrowserLaunchArguments.GetProfileRoot(browser, environment);
        LoginProfile profile;
        try
        {
            LoginProfile.SweepStale(root);
#pragma warning disable CA2000 // Disposed by the using declaration right after this try; nothing can throw in between.
            profile = LoginProfile.Create(root);
#pragma warning restore CA2000
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unwritable or blocked profile root: no browser can run, so the login falls back to pasting.
            var reason = $"Could not create the temporary browser profile: {ex.Message.TrimEnd('.')}.";
            return await ReadPastedAddressAsync(reason, loginUrl, cancellationToken).ConfigureAwait(false);
        }

        using var ownedProfile = profile;

        IBrowserSession browserSession;
        try
        {
            browserSession = await launcher
                .LaunchAsync(browser, profile.DirectoryPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await DeleteProfileAsync(profile, ProfileDeleteAttempts, ProfileDeleteDelay).ConfigureAwait(false);
            throw;
        }
#pragma warning disable CA1031, S2221 // Whatever stopped the browser from starting, the login falls back to pasting.
        catch (Exception ex)
#pragma warning restore CA1031, S2221
        {
            // The profile goes first: the paste fallback can wait on the user for a long time.
            await DeleteProfileAsync(profile, ProfileDeleteAttempts, ProfileDeleteDelay).ConfigureAwait(false);
            var reason = $"Could not start {browser.Name}: {ex.Message.TrimEnd('.')}.";
            return await ReadPastedAddressAsync(reason, loginUrl, cancellationToken).ConfigureAwait(false);
        }

#pragma warning disable CA2000 // Disposed in the finally below, after Browser.close and before the browser session.
        var cdp = new CdpSession(browserSession.Transport);
#pragma warning restore CA2000
        try
        {
            return await RustPlusAppLogin.RunAsync(cdp, loginUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (CdpException ex)
        {
            // CdpException is internal; its message is the browser's own error text.
            throw new InvalidOperationException(BrowserRejectedMessage, ex);
        }
        finally
        {
            // None of these take the caller's token, and none throws: cleanup runs in full on every path,
            // cancellation included, and never replaces the login's own result or exception.
            await CloseBrowserAsync(cdp).ConfigureAwait(false);
            await DisposeQuietlyAsync(cdp).ConfigureAwait(false);
            await DisposeQuietlyAsync(browserSession).ConfigureAwait(false);
            await DeleteProfileAsync(profile, ProfileDeleteAttempts, ProfileDeleteDelay).ConfigureAwait(false);
        }
    }

    /// <summary>Builds the Facepunch login URL that redirects back to <paramref name="returnUrl"/>
    /// with <c>steamId</c> and <c>token</c> appended as query parameters.</summary>
    /// <param name="returnUrl">Where Facepunch should send the browser after the Steam sign-in: an absolute URL, or a
    /// path on Facepunch's own site such as <see cref="RegistrationConstants.RustPlusAppReturnUrl"/>, which the
    /// library itself uses.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="returnUrl"/> is blank.</exception>
#pragma warning disable CA1055, CA1054 // Return and parameter types must be string to match the callback parsing contract
    public static string BuildLoginUrl(string returnUrl) =>
        BuildLoginUrl(RegistrationConstants.SteamLoginUrl, returnUrl);
#pragma warning restore CA1055, CA1054

    /// <summary>Builds the login URL against an arbitrary base (the Facepunch login in production).</summary>
    /// <param name="loginUrlBase">The login page to send the user to.</param>
    /// <param name="returnUrl">Where Facepunch should send the browser after the Steam sign-in: an absolute URL, or a
    /// path on Facepunch's own site.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="returnUrl"/> is blank.</exception>
    internal static string BuildLoginUrl(string loginUrlBase, string returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            throw new ArgumentException("The return URL must not be blank.", nameof(returnUrl));
        }

        return loginUrlBase + "?returnUrl=" + Uri.EscapeDataString(returnUrl);
    }

    /// <summary>Extracts the Steam identity from the callback Facepunch redirects the browser to.</summary>
    /// <param name="callbackUri">The full callback URI, including its query string.</param>
    /// <exception cref="InvalidOperationException">Thrown when the callback carries no usable
    /// <c>token</c> or <c>steamId</c> — i.e. when Facepunch has changed the callback contract.</exception>
    public static SteamLoginResult ParseCallback(Uri callbackUri)
    {
        var query = ParseQuery(callbackUri.Query);

        // Narrow with a pattern rather than string.IsNullOrEmpty: netstandard2.0's reference assembly
        // lacks the [NotNullWhen(false)] annotation, so only the pattern proves non-nullness on both TFMs.
        query.TryGetValue("token", out var token);
        if (token is not { Length: > 0 } || token.Trim().Length == 0)
        {
            throw new InvalidOperationException(
                "The Facepunch login callback carried no 'token' parameter. The login contract has likely "
                + "changed upstream — re-check RegistrationConstants against rustplus.js.");
        }

        if (!query.TryGetValue("steamId", out var steamIdText)
            || !ulong.TryParse(steamIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var steamId))
        {
            throw new InvalidOperationException(
                "The Facepunch login callback carried no usable 'steamId' parameter. The login contract has "
                + "likely changed upstream — re-check RegistrationConstants against rustplus.js.");
        }

        return new SteamLoginResult(steamId, token);
    }

    /// <summary>Extracts the Steam identity from a pasted <c>rust-plus://rust-auth-session</c> address,
    /// tolerating surrounding whitespace, double quotes and a leading <c>Location:</c> label.</summary>
    /// <param name="pasted">The text the user copied.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pasted"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the text is not the Rust+ login redirect,
    /// or carries no usable <c>token</c> or <c>steamId</c>.</exception>
    public static SteamLoginResult ParsePastedAddress(string pasted)
    {
#pragma warning disable CA1510 // ArgumentNullException.ThrowIfNull is not available on netstandard2.0
        if (pasted is null)
        {
            throw new ArgumentNullException(nameof(pasted));
        }
#pragma warning restore CA1510

        var value = pasted.Trim();
        const string label = "location:";
        if (value.StartsWith(label, StringComparison.OrdinalIgnoreCase))
        {
            value = value[label.Length..].Trim();
        }

        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }

        // A text with the right prefix can still fail to parse (a space in it, a bad port): that is the same mistake
        // to the user, and the message stays fixed because the text carries the token.
        if (!value.StartsWith(RegistrationConstants.RustPlusAuthRedirectPrefix, StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(value, UriKind.Absolute, out var address))
        {
            throw new InvalidOperationException(NotTheLoginAddressMessage);
        }

        return ParseCallback(address);
    }

    /// <summary>Releases and deletes a login profile, retrying while the browser still holds its files. A profile that
    /// cannot be deleted is left for the next login's sweep: a valid login must not fail because of it.</summary>
    /// <param name="profile">The profile to delete.</param>
    /// <param name="attempts">The maximum number of delete attempts.</param>
    /// <param name="delay">The wait between attempts.</param>
    internal static async Task DeleteProfileAsync(LoginProfile profile, int attempts, TimeSpan delay)
    {
        if (!await profile.TryDeleteAsync(attempts, delay).ConfigureAwait(false))
        {
            Debug.WriteLine(
                $"[{nameof(SteamLoginService)}] Could not delete the temporary browser profile '{profile.DirectoryPath}'; "
                + "the next login removes it.");
        }
    }

    /// <summary>Asks the browser to close, within its own timeout. A window that is already gone, or a browser that
    /// does not answer in time, is fine: disposing the browser session ends the process either way.</summary>
    /// <param name="cdp">The DevTools session.</param>
    private static async Task CloseBrowserAsync(CdpSession cdp)
    {
        using var timeout = new CancellationTokenSource(BrowserCloseTimeout);
        try
        {
            await cdp.SendAsync("Browser.close", null, timeout.Token).ConfigureAwait(false);
        }
#pragma warning disable CA1031, S2221 // Best effort: the browser session's disposal ends the process regardless.
        catch (Exception ex)
#pragma warning restore CA1031, S2221
        {
            Debug.WriteLine(
                $"[{nameof(SteamLoginService)}] The browser did not confirm it was closing ({ex.GetType().Name}).");
        }
    }

    /// <summary>Disposes a cleanup step without letting its failure escape. Only the exception type is logged.</summary>
    /// <param name="disposable">The resource to dispose.</param>
    private static async Task DisposeQuietlyAsync(IAsyncDisposable disposable)
    {
        try
        {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031, S2221 // Cleanup must never replace the login's own result or exception.
        catch (Exception ex)
#pragma warning restore CA1031, S2221
        {
            Debug.WriteLine(
                $"[{nameof(SteamLoginService)}] Could not shut the browser down cleanly ({ex.GetType().Name}).");
        }
    }

    /// <summary>The paste fallback: reads the address the user copied by hand, or explains how to get it.</summary>
    /// <param name="reason">Why no browser can be driven, as a full sentence. Passed to the paste callback, or it
    /// starts the exception message.</param>
    /// <param name="loginUrl">The login URL the user must open.</param>
    /// <param name="cancellationToken">Passed to the paste callback.</param>
    /// <exception cref="InvalidOperationException">Thrown when no paste callback is set, or the pasted text is not
    /// the Rust+ login address.</exception>
    private async Task<SteamLoginResult> ReadPastedAddressAsync(string reason,
        string loginUrl,
        CancellationToken cancellationToken)
    {
        var read = Options.ReadPastedAddress
                   ?? throw new InvalidOperationException($"{reason} {PasteInstructions} Login address: {loginUrl}");
        return ParsePastedAddress(await read(reason, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Parses a URI query string into its decoded key/value pairs. Later duplicates win.</summary>
    /// <param name="query">The query string, with or without its leading <c>?</c>.</param>
    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var trimmed = query.TrimStart('?');
        if (trimmed.Length == 0)
        {
            return result;
        }

        foreach (var pair in trimmed.Split('&'))
        {
            if (pair.Length == 0)
            {
                continue;
            }

#pragma warning disable CA1307 // string.IndexOf(char, StringComparison) is not available on netstandard2.0
            var separator = pair.IndexOf('=');
#pragma warning restore CA1307
            if (separator < 0)
            {
                result[WebUtility.UrlDecode(pair)] = string.Empty;
                continue;
            }

            var key = WebUtility.UrlDecode(pair[..separator]);
            result[key] = WebUtility.UrlDecode(pair[(separator + 1)..]);
        }

        return result;
    }
}
