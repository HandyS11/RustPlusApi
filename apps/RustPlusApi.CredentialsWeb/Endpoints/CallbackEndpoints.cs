using RustPlusApi.CredentialsWeb.Flow;
using RustPlusApi.CredentialsWeb.Sessions;
using RustPlusApi.Fcm.Registration;
using RustPlusApi.Fcm.Registration.Steps;
using System.Diagnostics.CodeAnalysis;

namespace RustPlusApi.CredentialsWeb.Endpoints;

/// <summary>The address the visitor copied out of their browser's error page.</summary>
/// <param name="Url">The pasted address, exactly as they gave it.</param>
internal sealed record PasteCallbackRequest(string? Url);

/// <summary>Tells the browser which session the paste belonged to, so a tab that lost its handle can
/// pick the flow back up.</summary>
/// <param name="SessionId">The session the return token identified.</param>
internal sealed record PasteCallbackResponse(string SessionId);

/// <summary>The Facepunch redirect target.</summary>
internal static class CallbackEndpoints
{
    private const string ConsumedMessage =
        "That address was already used, or the session expired. Start over — nothing was saved.";

    private const string UnreadableMessage =
        "That doesn't look like the Rust+ callback address. Copy the whole address from the page "
        + "that failed to load, starting with http://, and try again.";

    /// <summary>Maps <c>GET /callback/{returnToken}</c> and <c>POST /api/callback</c>.</summary>
    /// <param name="app">The route builder.</param>
    internal static void MapCallbackEndpoints(this IEndpointRouteBuilder app)
    {
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
                    $"{context.Request.Scheme}://{context.Request.Host}",
                    context.Request.Path,
                    context.Request.QueryString,
                    out var login))
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

        app.MapPost("/api/callback", (
            PasteCallbackRequest request,
            SessionStore store,
            CredentialFlow flow) =>
        {
            // Parse before consuming, unlike the GET route. The visitor is looking at this response,
            // so a fumbled paste has to leave the session intact for them to correct.
            if (!CallbackParsing.TryParsePastedCallback(request.Url, out var returnToken, out var login))
            {
                return Results.Json(new ErrorPayload(UnreadableMessage), statusCode: 400);
            }

            // Single-use, exactly as for the redirect: an address pasted twice finds nothing, and an
            // unknown token is indistinguishable from a consumed one.
            if (!store.TryConsumeReturnToken(returnToken, out var session))
            {
                return Results.Json(new ErrorPayload(ConsumedMessage), statusCode: 404);
            }

            session.BackgroundWork = flow.CompleteRegistrationAsync(session, login, session.Lifetime.Token);
            return Results.Json(new PasteCallbackResponse(session.SessionId), statusCode: 202);
        });
    }

    /// <summary>Builds the callback URI from the request and parses the Steam identity out of it.
    /// Extracted from the route handler so <see cref="UriFormatException"/> — essentially
    /// unreachable through a real request, since <paramref name="publicBaseUrl"/> is built from a
    /// request Kestrel has already accepted, so the path and query it is concatenated with cannot be
    /// URI-illegal by the time this handler runs — can still be forced directly in a unit test with a
    /// deliberately malformed <paramref name="publicBaseUrl"/>, with no <c>TestServer</c>
    /// involved.</summary>
    /// <param name="publicBaseUrl">The origin this request arrived on.</param>
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
