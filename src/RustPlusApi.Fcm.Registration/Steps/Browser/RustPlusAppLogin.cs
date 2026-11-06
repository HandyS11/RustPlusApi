using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using RustPlusApi.Fcm.Registration.Steps.Cdp;

namespace RustPlusApi.Fcm.Registration.Steps.Browser;

/// <summary>
/// Drives one Rust+ companion login over an already-connected DevTools session. Facepunch ends the login
/// with <c>/app</c> answering <c>302 Location: rust-plus://rust-auth-session?steamId=…&amp;token=…</c>; the
/// token is read from that redirect and the browser is handed a small "Done" page instead, so it never
/// navigates to <c>rust-plus:</c> (which would launch the official Rust+ desktop app).
/// </summary>
internal static class RustPlusAppLogin
{
    /// <summary>The page the browser is shown once the token has been captured.</summary>
    internal const string DoneHtml =
        "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Done</title></head>"
        + "<body><p>Done. You can close this window and go back to your terminal.</p></body></html>";

    private const string WindowClosedMessage = "The browser window was closed before the Steam login finished.";
    private const string RefusalMessage = "Facepunch refused the Steam login at its last step";
    private const string RustPlusScheme = "rust-plus:";
    private const string FetchEnable = "Fetch.enable";
    private const string RequestIdKey = "requestId";
    private const string AppPath = "/app";

    /// <summary>Runs one login: intercepts only the Facepunch <c>/app</c> and <c>/login-failed</c> requests,
    /// navigates to <paramref name="loginUrl"/> and waits for the final redirect.</summary>
    /// <param name="session">A connected DevTools session attached to the page.</param>
    /// <param name="loginUrl">The Facepunch login URL.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The Steam ID and Rust+ token from the final redirect.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="CdpException">The browser rejected a command (an error reply, not a closed window).</exception>
    /// <exception cref="InvalidOperationException">The window closed, Facepunch refused the login, or the redirect was unusable.
    /// Messages never contain the token, query strings or header values.</exception>
    internal static async Task<SteamLoginResult> RunAsync(CdpSession session,
        string loginUrl,
        CancellationToken cancellationToken)
    {
        await SendAsync(session, FetchEnable, new JsonObject
        {
            ["patterns"] = new JsonArray(
                Pattern(RegistrationConstants.CompanionAppUrlPrefix, "Request"),
                Pattern(RegistrationConstants.CompanionAppUrlPrefix, "Response"),
                Pattern(RegistrationConstants.CompanionLoginFailedUrlPrefix, "Request")),
        }, cancellationToken).ConfigureAwait(false);
        await SendAsync(session, "Page.navigate", new JsonObject
        {
            ["url"] = loginUrl
        }, cancellationToken).ConfigureAwait(false);

        var rewroteWindowsUserAgent = false;
        while (true)
        {
            var evt = await session.ReadEventAsync(cancellationToken).ConfigureAwait(false)
                      ?? throw new InvalidOperationException(WindowClosedMessage);
            if (evt.Method != "Fetch.requestPaused" || evt.Params is not JsonObject paused)
            {
                continue;
            }

            var requestId = (string?)paused[RequestIdKey];
            var url = (string?)paused["request"]?["url"];
            if (requestId is null || url is null)
            {
                continue;
            }

            // The /app* pattern also matches resources such as /app.js or /apple-touch-icon.png; only /app itself is
            // Facepunch's last step, the one to rewrite the user agent for and to judge the answer of.
            var isAppStep = IsAppStep(url);
            if (paused["responseStatusCode"] is JsonValue statusValue && statusValue.TryGetValue(out int status))
            {
                var result = await HandleResponseAsync(session, requestId, status, paused["responseHeaders"],
                        isAppStep, rewroteWindowsUserAgent, cancellationToken)
                    .ConfigureAwait(false);
                if (result is not null)
                {
                    return result;
                }
            }
            else if (url.StartsWith(RegistrationConstants.CompanionLoginFailedUrlPrefix, StringComparison.Ordinal))
            {
                await SendAsync(session, "Fetch.continueRequest", new JsonObject
                    {
                        [RequestIdKey] = requestId
                    }, cancellationToken)
                    .ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"Facepunch showed its Login Failed page (reason: {ReasonOf(url)}). Start the login again.");
            }
            else
            {
                // Without headers the request is continued untouched.
                var headers = isAppStep ? paused["request"]!["headers"] : null;
                var rewrote = await ContinueRequestAsync(session, requestId, headers, cancellationToken)
                    .ConfigureAwait(false);
                if (rewrote)
                {
                    rewroteWindowsUserAgent = true;
                }
            }
        }
    }

    /// <summary>Whether a paused URL is Facepunch's <c>/app</c> endpoint itself, compared ordinally.</summary>
    /// <param name="url">The paused request's URL.</param>
    private static bool IsAppStep(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.AbsolutePath == AppPath;

    private static JsonObject Pattern(string prefix, string stage) =>
        new()
        {
            ["urlPattern"] = prefix + "*", ["requestStage"] = stage
        };

    private static async Task<bool> ContinueRequestAsync(
        CdpSession session,
        string requestId,
        JsonNode? requestHeaders,
        CancellationToken cancellationToken)
    {
        // Continues a request-stage pause, replacing only the user agent and platform hint when the
        // browser says Windows. Returns whether it rewrote.
        var parameters = new JsonObject
        {
            [RequestIdKey] = requestId
        };
        var rewrite = requestHeaders is JsonObject headers &&
                      UserAgentRewriter.SaysWindows(HeaderValue(headers, "user-agent"));
        if (rewrite)
        {
            parameters["headers"] = RewriteHeaders((JsonObject)requestHeaders!);
        }

        await SendAsync(session, "Fetch.continueRequest", parameters, cancellationToken).ConfigureAwait(false);
        return rewrite;
    }

    private static JsonArray RewriteHeaders(JsonObject headers)
    {
        var rewritten = new JsonArray();
        foreach (var header in headers)
        {
            var name = header.Key;
            var value = header.Value?.ToString() ?? string.Empty;
            if (name.Equals("user-agent", StringComparison.OrdinalIgnoreCase))
            {
                value = UserAgentRewriter.ToNonWindows(value);
            }
            else if (name.Equals("sec-ch-ua-platform", StringComparison.OrdinalIgnoreCase))
            {
                value = UserAgentRewriter.NonWindowsPlatformHint;
            }

            rewritten.Add(new JsonObject
            {
                ["name"] = name, ["value"] = value
            });
        }

        return rewritten;
    }

    private static async Task<SteamLoginResult?> HandleResponseAsync(
        CdpSession session,
        string requestId,
        int status,
        JsonNode? responseHeaders,
        bool isAppStep,
        bool rewroteWindowsUserAgent,
        CancellationToken cancellationToken)
    {
        // Handles a response-stage pause. Returns the result for the final redirect, null to keep waiting, and
        // throws when Facepunch refused. A rust-plus: redirect is captured whatever path issued it (it must never be
        // continued); any other answer from outside /app itself is continued and never judged.
        if (status is >= 300 and < 400)
        {
            var location = LocationOf(responseHeaders);
            if (location?.StartsWith(RustPlusScheme, StringComparison.OrdinalIgnoreCase) == true)
            {
                // Any rust-plus: redirect is the final one and is never continued, even if it cannot be parsed:
                // continuing would hand the link to the OS and launch the official Rust+ desktop app. On a parse
                // failure the request stays paused and the caller closes the browser.
                var result = Parse(location);
                await FulfilDonePageAsync(session, requestId, cancellationToken).ConfigureAwait(false);
                return result;
            }

            await ContinueResponseStageAsync(session, requestId, cancellationToken).ConfigureAwait(false);
            return null;
        }

        await ContinueResponseStageAsync(session, requestId, cancellationToken).ConfigureAwait(false);
        if (!isAppStep)
        {
            return null;
        }

        if (status is < 200 or >= 300)
        {
            throw new InvalidOperationException(
                $"Facepunch answered HTTP {status.ToString(CultureInfo.InvariantCulture)} at its last step.");
        }

        throw new InvalidOperationException(rewroteWindowsUserAgent
            ? RefusalMessage +
              " (it answered with a page instead of the sign-in redirect), although the browser was reported as non-Windows. Facepunch may have changed its login."
            : RefusalMessage +
              " (it answered with a page instead of the sign-in redirect). Facepunch may have changed its login.");
    }

    private static SteamLoginResult Parse(string location)
    {
        Uri uri;
        try
        {
            uri = new Uri(location);
        }
        catch (UriFormatException)
        {
            // Deliberately without the inner exception or the value: the location carries the token.
            throw new InvalidOperationException("The Facepunch login redirect was not a valid address.");
        }

        return SteamLoginService.ParseCallback(uri);
    }

    private static async Task ContinueResponseStageAsync(CdpSession session,
        string requestId,
        CancellationToken cancellationToken)
    {
        // Continues a paused response. Chrome rejects continueRequest at the response stage on
        // some versions, so a rejected reply falls back to continueResponse.
        var parameters = new JsonObject
        {
            [RequestIdKey] = requestId
        };
        try
        {
            await SendAsync(session, "Fetch.continueRequest", parameters, cancellationToken).ConfigureAwait(false);
        }
        catch (CdpException)
        {
            await SendAsync(session, "Fetch.continueResponse", new JsonObject
                {
                    [RequestIdKey] = requestId
                }, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static Task
        FulfilDonePageAsync(CdpSession session, string requestId, CancellationToken cancellationToken) =>
        SendAsync(session, "Fetch.fulfillRequest", new JsonObject
        {
            [RequestIdKey] = requestId,
            ["responseCode"] = 200,
            ["responseHeaders"] = new JsonArray(
                new JsonObject
                {
                    ["name"] = "Content-Type", ["value"] = "text/html; charset=utf-8"
                },
                new JsonObject
                {
                    ["name"] = "Cache-Control", ["value"] = "no-store"
                }),
            ["body"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(DoneHtml)),
        }, cancellationToken);

    private static async Task SendAsync(CdpSession session,
        string method,
        JsonObject parameters,
        CancellationToken cancellationToken)
    {
        // Sends a command; a failure that coincides with the connection being closed means the user
        // closed the window, whereas a plain error reply propagates unchanged.
        try
        {
            await session.SendAsync(method, parameters, cancellationToken).ConfigureAwait(false);
        }
        catch (CdpException ex) when (session.IsClosed)
        {
            throw new InvalidOperationException(WindowClosedMessage, ex);
        }
    }

    private static string? HeaderValue(JsonObject headers, string name) =>
        headers.FirstOrDefault(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value?.ToString();

    private static string? LocationOf(JsonNode? responseHeaders) =>
        responseHeaders is JsonArray entries
            ? (string?)entries.FirstOrDefault(e =>
                ((string?)e?["name"])?.Equals("location", StringComparison.OrdinalIgnoreCase) == true)?["value"]
            : null;

    private static string ReasonOf(string url)
    {
        // Extracts the reason query value of the Login Failed page, only when it is a short
        // plain word, so that nothing arbitrary from the URL reaches an exception message.
#pragma warning disable CA1307 // string.IndexOf(char, StringComparison) is not available on netstandard2.0
        var queryStart = url.IndexOf('?');
#pragma warning restore CA1307
        var query = queryStart < 0 ? string.Empty : url[(queryStart + 1)..];
        foreach (var pair in query.Split('&').Where(p => p.StartsWith("reason=", StringComparison.Ordinal)))
        {
            var reason = WebUtility.UrlDecode(pair["reason=".Length..]);
            if (reason.Length is > 0 and <= 40 && reason.All(c => char.IsLetterOrDigit(c) || c is '-' or '_'))
            {
                return reason;
            }
        }

        return "unknown";
    }
}
