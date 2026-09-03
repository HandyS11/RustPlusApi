using RustPlusApi.CredentialsWeb.Sessions;
using System.Text.Json;

namespace RustPlusApi.CredentialsWeb.Endpoints;

/// <summary>The server-to-client push channel. One-directional by design: the only thing the server
/// ever tells the browser is where the flow has got to.</summary>
internal static class EventEndpoints
{
    /// <summary>Maps <c>GET /api/sessions/{sessionId}/events</c>.</summary>
    /// <param name="app">The route builder.</param>
    internal static void MapEventEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/sessions/{sessionId}/events", async (
            string sessionId,
            HttpContext context,
            SessionStore store,
            CancellationToken cancellationToken) =>
        {
            if (!store.TryGet(sessionId, out var session))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "text/event-stream";
            // Tells nginx not to buffer the stream; without it a proxy can hold events for minutes.
            context.Response.Headers["X-Accel-Buffering"] = "no";
            await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

            await foreach (var sessionEvent in session.Events
                               .SubscribeAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
#pragma warning disable VSTHRD103 // Serializing an in-memory payload record is pure CPU work with
                // no I/O to await; SerializeAsync would only add overhead for a string this small.
                var json = sessionEvent.Data is null
                    ? "{}"
                    : JsonSerializer.Serialize(sessionEvent.Data, JsonSerializerOptions.Web);
#pragma warning restore VSTHRD103

                await context.Response
                    .WriteAsync($"event: {sessionEvent.Type}\ndata: {json}\n\n", cancellationToken)
                    .ConfigureAwait(false);
                await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        });
}
