using RustPlusApi.Fcm.Registration;
using RustPlusApi.Fcm.Registration.Steps;
using System.Diagnostics.CodeAnalysis;

namespace RustPlusApi.CredentialsWeb.Endpoints;

/// <summary>Reads a Facepunch callback address the visitor pasted in, rather than one their browser
/// delivered. Deliberately pure — no session lookup, no side effect — so a fumbled paste is rejected
/// before any single-use token is consumed and the visitor can simply try again.</summary>
internal static class CallbackParsing
{
    /// <summary>Splits a pasted address into the return token that identifies the session and the
    /// Steam identity Facepunch appended to it.</summary>
    /// <param name="pasted">Whatever the visitor put in the box.</param>
    /// <param name="returnToken">The single-use token from the address's last path segment.</param>
    /// <param name="login">The Steam identity carried in the query string.</param>
    internal static bool TryParsePastedCallback(
        string? pasted,
        [NotNullWhen(true)] out string? returnToken,
        [NotNullWhen(true)] out SteamLoginResult? login)
    {
        returnToken = null;
        login = null;

        if (string.IsNullOrWhiteSpace(pasted))
        {
            return false;
        }

        var trimmed = pasted.Trim();

        // Safari copies an address without its scheme, so a scheme-less paste gets one. The test for
        // "already has a scheme" is "://" rather than "parses as an absolute URI", because
        // "localhost:54321/callback/..." parses as an absolute URI whose *scheme* is "localhost" —
        // that is the case the prefix exists for. Prepending to something that does name a scheme
        // would be worse than useless: "http://" + "ftp://host/p" parses cleanly as host "ftp" with
        // the rest as path, so a scheme this method rejects would sneak back in looking valid.
#pragma warning disable S5332 // Not a transport choice: this is a loopback callback address the
        // visitor's own browser was redirected to, and it is parsed, never fetched.
        var candidate = trimmed.Contains("://", StringComparison.Ordinal)
            ? trimmed
            : $"http://{trimmed}";
#pragma warning restore S5332

        if (!TryAsWebUri(candidate, out var uri))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || !IsReturnToken(segments[^1]))
        {
            return false;
        }

        try
        {
            login = SteamLoginService.ParseCallback(uri!);
        }
        catch (InvalidOperationException)
        {
            // No usable token or steamId: the Facepunch login URL pasted by mistake, a truncated
            // copy, or a contract change upstream. All three are the visitor's cue to try again.
            return false;
        }

        returnToken = segments[^1];
        return true;
    }

    /// <summary>True for 32 lowercase hex characters, which is what <see cref="Sessions.SessionIds"/>
    /// produces.</summary>
    /// <param name="value">The candidate path segment.</param>
    private static bool IsReturnToken(string value)
    {
        if (value.Length != 32)
        {
            return false;
        }

#pragma warning disable S3267 // early return from loop is clearer than LINQ equivalent here
        foreach (var character in value)
        {
            if (character is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

#pragma warning restore S3267
        return true;
    }

    /// <summary>Parses an absolute <c>http</c> or <c>https</c> URI, rejecting every other scheme.</summary>
    /// <param name="value">The candidate address.</param>
    /// <param name="uri">The parsed URI on success.</param>
    private static bool TryAsWebUri(string value, [NotNullWhen(true)] out Uri? uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri)
        && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal));
}
