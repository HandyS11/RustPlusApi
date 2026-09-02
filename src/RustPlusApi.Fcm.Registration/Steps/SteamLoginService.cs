using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace RustPlusApi.Fcm.Registration.Steps;

/// <summary>
/// Step 5 — interactive Steam login. Sends the user's own browser to the Facepunch login page with
/// a loopback <c>returnUrl</c>, and captures the <c>steamId</c> and <c>token</c> Facepunch appends
/// when it redirects back.
/// </summary>
/// <param name="port">The loopback port the callback listener binds to; <c>0</c> picks a free one.</param>
/// <remarks>
/// Any browser works — the flow is an ordinary redirect, with no page scripting involved. The
/// callback path carries a per-run nonce so a page the user happens to be browsing cannot feed a
/// token of its own choosing into the loopback listener.
/// </remarks>
public sealed class SteamLoginService(int port = 3000)
{
    private const string SuccessHtml =
        "<!doctype html><meta charset=\"utf-8\"><title>Rust+ login complete</title>"
        + "<script>history.replaceState(null,'',location.pathname);</script>"
        + "<h1>Done. You can close this window.</h1>";

    private const string FailureHtml =
        "<!doctype html><meta charset=\"utf-8\"><title>Rust+ login failed</title>"
        + "<h1>That callback carried no Rust+ token. Try the login link again.</h1>";

    /// <summary>Builds the 404 body for an unrecognised callback path, naming both the expected and
    /// the received path so a Facepunch contract change (path stripped, normalised, or otherwise
    /// altered by the Steam OpenID round-trip) is diagnosable instead of presenting as a silent
    /// hang while the token sits unconsumed in the browser's URL bar.</summary>
    /// <param name="expectedPath">The per-run callback path this listener actually expects.</param>
    /// <param name="actualPath">The path the request carried instead.</param>
    private static string BuildNotFoundHtml(string expectedPath, string actualPath) =>
        "<!doctype html><meta charset=\"utf-8\"><title>Rust+ login</title>"
        + "<h1>Unknown callback path.</h1>"
        + $"<p>Expected <code>{WebUtility.HtmlEncode(expectedPath)}</code> but received "
        + $"<code>{WebUtility.HtmlEncode(actualPath)}</code>.</p>";

    /// <summary>Sends the user's browser to the Facepunch Steam login and returns the captured identity.</summary>
    /// <param name="onLoginUrl">Invoked with the login URL before the browser is opened, so callers
    /// can print it. Always invoked, including when a browser is opened successfully.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled before a callback arrives.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the callback port cannot be bound.</exception>
    public Task<SteamLoginResult> LoginAsync(Action<string>? onLoginUrl = null,
        CancellationToken cancellationToken = default) =>
        LoginAsync(RegistrationConstants.SteamLoginUrl, onLoginUrl, openBrowser: true, cancellationToken);

    /// <summary>Drives the login against an arbitrary login page, optionally without opening a browser.</summary>
    /// <param name="loginUrlBase">The login page to send the user to.</param>
    /// <param name="onLoginUrl">Invoked with the login URL before any browser is opened.</param>
    /// <param name="openBrowser">Whether to attempt to open the user's default browser.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled before a callback arrives.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the callback port cannot be bound.</exception>
    internal async Task<SteamLoginResult> LoginAsync(string loginUrlBase,
        Action<string>? onLoginUrl,
        bool openBrowser,
        CancellationToken cancellationToken)
    {
        // HttpListener cannot bind port 0, so a free port is resolved up front by opening and
        // immediately closing a probe socket on it (GetFreePort). The window between that probe
        // closing and listener.Start() below binding the same port is inherently racy — another
        // process could claim the port in between — but there is no API to resolve and bind a free
        // port as one atomic step.
        var boundPort = port == 0 ? GetFreePort() : port;
        var nonce = CreateNonce();
        var callbackPath = "/callback/" + nonce;
#pragma warning disable S6618 // string.Create(IFormatProvider, ...) needs DefaultInterpolatedStringHandler, unavailable on netstandard2.0
        var returnUrl = FormattableString.Invariant($"http://localhost:{boundPort}{callbackPath}");

        using var listener = new HttpListener();
        listener.Prefixes.Add(FormattableString.Invariant($"http://localhost:{boundPort}/"));
#pragma warning restore S6618

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            var guidance = port == 0
                ? "It was free when probed via GetFreePort, but something else claimed it before the "
                  + "listener could bind — an inherent race, since there is no API to probe and bind a "
                  + "free port as one atomic step. Retry; a fresh free port is probed on each attempt."
                : $"Another process is probably using port {boundPort} — pass steamLoginPort: 0 to pick a "
                  + "free port automatically.";
            throw new InvalidOperationException(
                $"Could not bind the Steam login callback listener to http://localhost:{boundPort}/. "
                + guidance, ex);
        }

        // GetContextAsync takes no cancellation token: stopping the listener is the only way to
        // unblock the wait promptly, so cancellation must not have to wait for the next request.
#pragma warning disable RCS1261 // CancellationTokenRegistration.DisposeAsync is not available on netstandard2.0
        using var cancellationRegistration = cancellationToken.Register(listener.Stop);
#pragma warning restore RCS1261

        try
        {
            var loginUrl = BuildLoginUrl(loginUrlBase, returnUrl);
            onLoginUrl?.Invoke(loginUrl);
            if (openBrowser)
            {
                TryOpenBrowser(loginUrl);
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException
                                               or InvalidOperationException)
                {
                    // The listener was stopped under the wait — by the cancellation registration
                    // above (expected; rethrow as cancellation) or by an unrelated teardown. The
                    // InvalidOperationException case covers the narrow window between the loop
                    // condition observing "not yet cancelled" and this call actually starting:
                    // Stop() may already have run by then, and GetContextAsync on a stopped-but-not-
                    // disposed listener throws InvalidOperationException rather than one of the other
                    // two. If the token isn't actually cancelled, this is a genuine failure and the
                    // rethrow below preserves it unchanged.
                    cancellationToken.ThrowIfCancellationRequested();
                    throw;
                }

                var requestPath = context.Request.Url?.AbsolutePath ?? "(no path)";
                if (!string.Equals(requestPath, callbackPath, StringComparison.Ordinal))
                {
                    Debug.WriteLine(
                        $"[{nameof(SteamLoginService)}] Callback path mismatch: expected '{callbackPath}', "
                        + $"received '{requestPath}'.");
                    await RespondAsync(context, HttpStatusCode.NotFound,
                        BuildNotFoundHtml(callbackPath, requestPath)).ConfigureAwait(false);
                    continue;
                }

                SteamLoginResult result;
                try
                {
                    result = ParseCallback(context.Request.Url!);
                }
                catch (InvalidOperationException)
                {
                    await RespondAsync(context, HttpStatusCode.BadRequest, FailureHtml).ConfigureAwait(false);
                    continue;
                }

                await RespondAsync(context, HttpStatusCode.OK, SuccessHtml).ConfigureAwait(false);
                return result;
            }
        }
        finally
        {
            listener.Stop();
        }

        throw new OperationCanceledException(cancellationToken);
    }

    /// <summary>Builds the Facepunch login URL that redirects back to <paramref name="returnUrl"/>
    /// with <c>steamId</c> and <c>token</c> appended as query parameters.</summary>
    /// <param name="returnUrl">The absolute URL Facepunch should redirect the browser back to.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="returnUrl"/> is blank.</exception>
#pragma warning disable CA1055, CA1054 // Return and parameter types must be string to match the callback parsing contract
    public static string BuildLoginUrl(string returnUrl) =>
        BuildLoginUrl(RegistrationConstants.SteamLoginUrl, returnUrl);
#pragma warning restore CA1055, CA1054

    /// <summary>Builds the login URL against an arbitrary base (the Facepunch login in production).</summary>
    /// <param name="loginUrlBase">The login page to send the user to.</param>
    /// <param name="returnUrl">The absolute URL Facepunch should redirect the browser back to.</param>
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

        return new SteamLoginResult
        {
            SteamId = steamId, Token = token
        };
    }

    /// <summary>Generates the per-run callback nonce as lowercase hex.</summary>
    private static string CreateNonce()
    {
        var bytes = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);

        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var value in bytes)
        {
            builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>Opens the user's default browser at <paramref name="url"/>, ignoring any failure.</summary>
    /// <param name="url">The login URL to open.</param>
    /// <remarks>Excluded from coverage: launches a real browser process, and failure is by design
    /// unobservable — the URL has already been reported through <c>onLoginUrl</c>, so a headless
    /// host simply opens it by hand.</remarks>
    [ExcludeFromCodeCoverage]
    private static void TryOpenBrowser(string url)
    {
        try
        {
            ProcessStartInfo startInfo;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                startInfo = new ProcessStartInfo(url)
                {
                    UseShellExecute = true
                };
            }
            else
            {
                var opener = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open";
                startInfo = new ProcessStartInfo(opener, url)
                {
                    UseShellExecute = false
                };
            }

            Process.Start(startInfo)?.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[{nameof(SteamLoginService)}] Could not open a browser: {ex.Message}");
        }
    }

    private static int GetFreePort()
    {
#pragma warning disable CA2000 // TcpListener is not IDisposable; Stop() + Server.Dispose() is the correct cleanup pattern.
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
#pragma warning restore CA2000
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
            listener.Server.Dispose();
        }
    }

    /// <summary>Writes an HTML response to a callback request. The write is best-effort: the browser
    /// may already have dropped the connection (tab closed, navigation cancelled, or a local page
    /// issuing <c>fetch(...).then(c =&gt; c.abort())</c>), which surfaces as <see cref="HttpListenerException"/>
    /// or <see cref="IOException"/> from <c>OutputStream.WriteAsync</c>. That must not be fatal: on the
    /// success path the caller has already parsed a valid token before calling this, so a failed write
    /// must not lose it, and on the 404/400 paths it must not abort the accept loop either. Cancellation
    /// is never swallowed, and <see cref="HttpListenerResponse.Close()"/> always runs so the context is
    /// released regardless of how the write went.</summary>
    /// <param name="context">The listener context carrying the response to write to.</param>
    /// <param name="status">The HTTP status code to respond with.</param>
    /// <param name="html">The HTML body to write.</param>
    private static async Task RespondAsync(HttpListenerContext context, HttpStatusCode status, string html)
    {
        try
        {
            var buffer = Encoding.UTF8.GetBytes(html);
            context.Response.StatusCode = (int)status;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers[HttpResponseHeader.CacheControl] = "no-store";
            context.Response.ContentLength64 = buffer.Length;
#if NET10_0_OR_GREATER
            await context.Response.OutputStream.WriteAsync(buffer.AsMemory()).ConfigureAwait(false);
#else
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
#endif
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException)
        {
            Debug.WriteLine($"[{nameof(SteamLoginService)}] Could not write the callback response: {ex.Message}");
        }
        finally
        {
            // Close() itself can throw the same "connection already gone" exceptions as the write
            // above; swallow those here too so a dropped connection can never escape this method.
            try
            {
                context.Response.Close();
            }
            catch (Exception ex) when (ex is HttpListenerException or IOException)
            {
                Debug.WriteLine($"[{nameof(SteamLoginService)}] Could not close the callback response: {ex.Message}");
            }
        }
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

            var key = WebUtility.UrlDecode(pair.Substring(0, separator));
            result[key] = WebUtility.UrlDecode(pair.Substring(separator + 1));
        }

        return result;
    }
}
