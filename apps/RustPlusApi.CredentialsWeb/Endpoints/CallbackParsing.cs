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

        // The scheme is checked rather than assumed, and the prefixed form tried second, because
        // "localhost:54321/callback/..." is itself a well-formed absolute URI whose scheme is
        // "localhost" — so a scheme-less paste would otherwise parse into nonsense rather than fail.
#pragma warning disable S5332 // http used to accept loopback addresses from the visitor's browser redirect
        if (!TryAsWebUri(trimmed, out var uri) && !TryAsWebUri($"http://{trimmed}", out uri))
#pragma warning restore S5332
        {
            return false;
        }

        // Verify the host is actually a loopback address to reject URLs with mismatched schemes
        // (e.g., "http://ftp://localhost..." that might parse successfully).
        if (!RequestMode.IsLoopbackHost(uri.Host))
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
    private static bool IsReturnToken(string value) =>
        value.Length == 32 && value.All(character =>
            character is ((>= '0' and <= '9') or (>= 'a' and <= 'f')));

    /// <summary>Parses an absolute <c>http</c> or <c>https</c> URI, rejecting every other scheme.</summary>
    /// <param name="value">The candidate address.</param>
    /// <param name="uri">The parsed URI on success.</param>
    private static bool TryAsWebUri(string value, [NotNullWhen(true)] out Uri? uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri)
        && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal));
}
