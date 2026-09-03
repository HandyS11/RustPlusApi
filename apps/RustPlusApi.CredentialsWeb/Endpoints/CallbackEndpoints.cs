using RustPlusApi.CredentialsWeb.Flow;
using RustPlusApi.CredentialsWeb.Sessions;
using RustPlusApi.Fcm.Registration.Steps;

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
            AppOptions options) =>
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

            try
            {
                var callbackUri = new Uri(
                    options.PublicBaseUrl + context.Request.Path + context.Request.QueryString);
                var login = SteamLoginService.ParseCallback(callbackUri);

                session.BackgroundWork = flow.CompleteRegistrationAsync(
                    session,
                    login,
                    session.Lifetime.Token);
            }
            catch (Exception ex) when (ex is InvalidOperationException or UriFormatException)
            {
                // ParseCallback rejects a callback with no usable token or steamId, and a malformed
                // request (e.g. an unparsable query string) can make the Uri construction above throw
                // UriFormatException instead. Both must land the session in Failed rather than crash
                // the host with a 500. The message is not surfaced: the page says the login did not
                // complete and offers a restart.
                session.Advance(SessionState.Failed, session.ExpiresAt);
                session.Events.Publish(new SessionEvent("error", new ErrorPayload(
                    "The Steam login didn't complete. Start over — nothing was saved.")));
            }

            return redirect;
        });
}
