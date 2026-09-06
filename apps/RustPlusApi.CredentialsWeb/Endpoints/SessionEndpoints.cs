using RustPlusApi.CredentialsWeb.Flow;
using RustPlusApi.CredentialsWeb.Sessions;
using RustPlusApi.Fcm.Registration.Steps;

namespace RustPlusApi.CredentialsWeb.Endpoints;

/// <summary>What the browser needs to start a flow.</summary>
/// <param name="SessionId">The handle for the event stream and follow-up calls.</param>
/// <param name="LoginUrl">The Facepunch login URL to send the visitor to.</param>
internal sealed record CreateSessionResponse(string SessionId, string LoginUrl);

/// <summary>Session lifecycle endpoints.</summary>
internal static class SessionEndpoints
{
    /// <summary>The command the capacity messages point at. Carries both settings the app requires
    /// at startup: without them it exits 1 before serving anything, so a bare <c>docker run</c>
    /// would be advice that cannot work.</summary>
    private const string RunCommand =
        "docker run -p 127.0.0.1:8080:8080 -e CredentialsWeb__PublicBaseUrl=http://localhost:8080 "
        + "-e CredentialsWeb__AllowInsecureBaseUrl=true ghcr.io/handys11/rustplusapi-credentials";

    private const string OverCapacityMessage =
        "This instance is at capacity. Try again in a few minutes — or run your own: " + RunCommand;

    private const string ActiveSessionMessage =
        "You already have a session in progress. Reopen that tab, or wait a few minutes and try again.";

    private const string PairingBusyMessage =
        "This instance is already holding as many pairing listeners as it allows. Try again in a "
        + "few minutes — or run your own: " + RunCommand;

    /// <summary>Maps <c>POST /api/sessions</c> and <c>POST /api/sessions/{sessionId}/pairing</c>.</summary>
    /// <param name="app">The route builder.</param>
    internal static void MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/sessions", (HttpContext context, SessionStore store, AppOptions options) =>
        {
            if (!store.TryCreate(ClientAddress.Of(context), out var session, out var failure))
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

            var returnUrl = $"{options.PublicBaseUrl}/callback/{session.ReturnToken}";
            return Results.Ok(new CreateSessionResponse(
                session.SessionId,
                SteamLoginService.BuildLoginUrl(returnUrl)));
        });

        app.MapPost("/api/sessions/{sessionId}/pairing", (
            string sessionId,
            SessionStore store,
            CredentialFlow flow) =>
        {
            if (!store.TryGet(sessionId, out var session))
            {
                return Results.NotFound();
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
        });
    }
}
