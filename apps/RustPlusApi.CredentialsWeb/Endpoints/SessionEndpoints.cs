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
    private const string OverCapacityMessage =
        "This instance is at capacity. Try again in a few minutes — or run your own: "
        + "docker run -p 8080:8080 ghcr.io/handys11/rustplusapi-credentials";

    private const string PairingBusyMessage =
        "This instance is already holding as many pairing listeners as it allows. Try again in a "
        + "few minutes — or run your own: docker run -p 8080:8080 ghcr.io/handys11/rustplusapi-credentials";

    /// <summary>Maps <c>POST /api/sessions</c> and <c>POST /api/sessions/{sessionId}/pairing</c>.</summary>
    /// <param name="app">The route builder.</param>
    internal static void MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/sessions", (HttpContext context, SessionStore store, AppOptions options) =>
        {
            if (!store.TryCreate(ClientAddress.Of(context), out var session, out _))
            {
                return Results.Json(new ErrorPayload(OverCapacityMessage), statusCode: 429);
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
