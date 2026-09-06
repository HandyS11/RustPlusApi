using RustPlusApi.CredentialsWeb.Flow;
using RustPlusApi.CredentialsWeb.Sessions;
using RustPlusApi.Fcm.Registration.Steps;
using System.Security.Cryptography;

namespace RustPlusApi.CredentialsWeb.Endpoints;

/// <summary>What the browser needs to start a flow.</summary>
/// <param name="SessionId">The handle for the event stream and follow-up calls.</param>
/// <param name="LoginUrl">The Facepunch login URL to send the visitor to.</param>
/// <param name="CallbackMode">"redirect" when Facepunch can deliver the callback here by itself,
/// "paste" when the visitor has to bring the address back by hand.</param>
/// <param name="PairingAvailable">Whether this session may start the pairing wait.</param>
internal sealed record CreateSessionResponse(
    string SessionId,
    string LoginUrl,
    string CallbackMode,
    bool PairingAvailable);

/// <summary>Session lifecycle endpoints.</summary>
internal static class SessionEndpoints
{
    /// <summary>The command the capacity messages point at. The app has no required setting, so a
    /// bare run is now advice that actually works.</summary>
    private const string RunCommand =
        "docker run -p 127.0.0.1:8080:8080 ghcr.io/handys11/rustplusapi-credentials";

    private const string OverCapacityMessage =
        "This instance is at capacity. Try again in a few minutes — or run your own: " + RunCommand;

    private const string ActiveSessionMessage =
        "You already have a session in progress. Reopen that tab, or wait a few minutes and try again.";

    private const string PairingBusyMessage =
        "This instance is already holding as many pairing listeners as it allows. Try again in a "
        + "few minutes — or run your own: " + RunCommand;

    private const string RemotePairingMessage =
        "Waiting for a pairing needs a socket held open to Google for as long as it takes you to "
        + "alt-tab into Rust, so this instance doesn't offer it. Your credentials above are the part "
        + "you need. To get the four pairing values, run the app yourself: " + RunCommand;

    /// <summary>Maps <c>POST /api/sessions</c> and <c>POST /api/sessions/{sessionId}/pairing</c>.</summary>
    /// <param name="app">The route builder.</param>
    internal static void MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/sessions", CreateSession);
        app.MapPost("/api/sessions/{sessionId}/pairing", StartPairing);
    }

    /// <summary>Handles <c>POST /api/sessions</c>: opens a session and hands back the Facepunch login
    /// URL to send the visitor to.</summary>
    /// <param name="context">The request, which decides local versus remote and supplies the origin.</param>
    /// <param name="store">The session store.</param>
    /// <param name="options">The instance's settings.</param>
    private static IResult CreateSession(HttpContext context, SessionStore store, AppOptions options)
    {
        var isLocal = RequestMode.IsLocal(context);

        if (!store.TryCreate(ClientAddress.Of(context), isLocal, out var session, out var failure))
        {
            // ActiveSessionForIp means a resumable session already exists for this address —
            // "at capacity" would be false and would send the visitor into a five-minute wait
            // for no reason. GlobalLimit and HourlyLimit are genuine capacity/rate limits, so
            // they keep the existing message.
            var message = failure == SessionCreateFailure.ActiveSessionForIp
                ? ActiveSessionMessage
                : OverCapacityMessage;
            return Results.Json(new ErrorPayload(message), statusCode: 429);
        }

        // Local: the redirect can land here, so the return URL is this very request's origin.
        // Nothing is configured, so nothing can be configured wrong.
        //
        // Remote: Facepunch only honours a loopback returnUrl, and decides that from the URL's
        // shape rather than its reachability — their servers cannot reach a visitor's localhost
        // either. So it gets a loopback address nothing is listening on. The visitor's browser
        // fails to connect and shows the address, which they paste back at POST /api/callback.
        // The port comes from the dynamic range, so it is very unlikely to belong to something
        // the visitor actually runs; if it somehow does, the single-use return token means the
        // paste fails closed rather than the flow completing somewhere else.
        var returnUrl = isLocal
            ? $"{context.Request.Scheme}://{context.Request.Host}/callback/{session.ReturnToken}"
            : $"http://localhost:{RandomNumberGenerator.GetInt32(49152, 65536)}/callback/{session.ReturnToken}";

        return Results.Ok(new CreateSessionResponse(
            session.SessionId,
            SteamLoginService.BuildLoginUrl(returnUrl),
            isLocal ? "redirect" : "paste",
            isLocal || options.AllowRemotePairing));
    }

    /// <summary>Handles <c>POST /api/sessions/{sessionId}/pairing</c>: starts the background wait for
    /// an in-game pairing.</summary>
    /// <param name="sessionId">The session handle from the route.</param>
    /// <param name="store">The session store.</param>
    /// <param name="flow">The registration flow that owns the wait.</param>
    /// <param name="options">The instance's settings.</param>
    private static IResult StartPairing(
        string sessionId,
        SessionStore store,
        CredentialFlow flow,
        AppOptions options)
    {
        if (!store.TryGet(sessionId, out var session))
        {
            return Results.NotFound();
        }

        // The pairing wait is the one step that holds a long-lived socket per visitor. A public
        // instance has no reason to hold one for a stranger, so it is local-only unless the
        // operator opts in — which someone self-hosting on a LAN address will want to.
        if (!session.IsLocal && !options.AllowRemotePairing)
        {
            return Results.Json(new ErrorPayload(RemotePairingMessage), statusCode: 403);
        }

        // Advisory: it settles the ordinary 409 without starting anything, but it is a read of
        // a state a concurrent request can change a moment later. What actually holds a session
        // to one pairing wait is Session.TryClaimForPairing, which
        // CredentialFlow.WaitForPairingAsync takes before it touches the network.
        if (session.State != SessionState.Ready)
        {
            return Results.Conflict(new ErrorPayload(
                "This session is not ready to wait for a pairing."));
        }

        // The slot is taken here rather than inside the flow so that a refusal is a plain 429
        // with nothing started; CredentialFlow.WaitForPairingAsync always releases it.
        if (!store.TryAcquirePairingSlot())
        {
            return Results.Json(new ErrorPayload(PairingBusyMessage), statusCode: 429);
        }

        session.BackgroundWork = flow.WaitForPairingAsync(session, session.Lifetime.Token);
        return Results.Accepted();
    }
}
