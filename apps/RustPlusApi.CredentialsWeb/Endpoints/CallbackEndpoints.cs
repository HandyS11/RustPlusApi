using RustPlusApi.CredentialsWeb.Flow;
using RustPlusApi.CredentialsWeb.Sessions;
using RustPlusApi.Fcm.Registration;
using RustPlusApi.Fcm.Registration.Steps;
using System.Diagnostics.CodeAnalysis;

namespace RustPlusApi.CredentialsWeb.Endpoints;

/// <summary>The Facepunch redirect target.</summary>
internal static class CallbackEndpoints
{
    /// <summary>Maps <c>GET /callback/{returnToken}</c>.</summary>
    /// <param name="app">The route builder.</param>
    internal static void MapCallbackEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/callback/{returnToken}", (
            string returnToken,
            HttpContext context,
            SessionStore store,
            CredentialFlow flow,
            AppOptions options,
            TimeProvider timeProvider) =>
        {
            // Single-use: a callback URL replayed from browser history finds nothing, and an
            // unknown token is indistinguishable from a consumed one.
            if (!store.TryConsumeReturnToken(returnToken, out var session))
            {
                return Results.NotFound();
            }

            // 302 rather than 200: a redirect leaves no back-button entry, so the token-bearing URL
            // never becomes one. The session handle rides in the fragment, which browsers never send
            // to a server and which therefore cannot reach an access log or a Referer header.
            var redirect = Results.Redirect($"/#session={session.SessionId}");

            if (TryParseSteamLogin(
                    options.PublicBaseUrl, context.Request.Path, context.Request.QueryString, out var login))
            {
                session.BackgroundWork = flow.CompleteRegistrationAsync(
                    session,
                    login,
                    session.Lifetime.Token);
            }
            else
            {
                // ParseCallback rejects a callback with no usable token or steamId, and a malformed
                // request can make the URI construction inside TryParseSteamLogin throw
                // UriFormatException instead. Both must land the session in Failed rather than crash
                // the host with a 500. The message is not surfaced: the page says the login did not
                // complete and offers a restart.
                //
                // The deadline uses SessionTtl, not the session's original (short) CreatedTtl expiry:
                // every other Failed transition in CredentialFlow does the same. A real Steam
                // round-trip — two-factor authentication included — can consume most of CreatedTtl, so
                // reusing it here would risk the sweeper removing the session, and its error, before
                // the visitor's browser ever reads it back.
                session.Advance(SessionState.Failed, timeProvider.GetUtcNow().Add(options.SessionTtl));
                session.Events.Publish(new SessionEvent("error", new ErrorPayload(
                    "The Steam login didn't complete. Start over — nothing was saved.")));
            }

            return redirect;
        });

    /// <summary>Builds the callback URI from the request and parses the Steam identity out of it.
    /// Extracted from the route handler so <see cref="UriFormatException"/> — essentially
    /// unreachable through a real request, since <see cref="AppOptions.PublicBaseUrl"/> is validated
    /// absolute at startup and Kestrel has already rejected anything that would leave the path or
    /// query URI-illegal — can still be forced directly in a unit test with a deliberately malformed
    /// <paramref name="publicBaseUrl"/>, with no <c>TestServer</c> involved.</summary>
    /// <param name="publicBaseUrl">The externally reachable origin, from <see cref="AppOptions.PublicBaseUrl"/>.</param>
    /// <param name="path">The request path.</param>
    /// <param name="queryString">The request query string.</param>
    /// <param name="login">The parsed Steam identity on success.</param>
    internal static bool TryParseSteamLogin(
        string publicBaseUrl,
        PathString path,
        QueryString queryString,
        [NotNullWhen(true)] out SteamLoginResult? login)
    {
        try
        {
            var callbackUri = new Uri(publicBaseUrl + path + queryString);
            login = SteamLoginService.ParseCallback(callbackUri);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UriFormatException)
        {
            login = null;
            return false;
        }
    }
}
