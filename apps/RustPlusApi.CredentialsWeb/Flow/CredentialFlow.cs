using RustPlusApi.CredentialsWeb.Sessions;
using RustPlusApi.CredentialsWeb.Upstream;
using RustPlusApi.Fcm.Registration;
using System.Globalization;

namespace RustPlusApi.CredentialsWeb.Flow;

/// <summary>Drives the credential flow in the order 4 → 1,2,3 → 5, so that a real Steam login gates
/// every upstream call. An unauthenticated visitor costs one dictionary entry and nothing else.</summary>
/// <param name="steps">The upstream seam.</param>
/// <param name="store">The session registry, for completion accounting and pairing slots.</param>
/// <param name="options">TTLs and caps.</param>
/// <param name="timeProvider">Clock.</param>
/// <param name="logger">Diagnostics. Never receives a secret.</param>
internal sealed class CredentialFlow(
    IRegistrationSteps steps,
    SessionStore store,
    AppOptions options,
    TimeProvider timeProvider,
    ILogger<CredentialFlow> logger)
{
    /// <summary>Runs steps 1-3 then 5 for a session whose Steam login has just landed.</summary>
    /// <param name="session">The session the callback belonged to.</param>
    /// <param name="login">The parsed callback result.</param>
    /// <param name="cancellationToken">Token to cancel the flow.</param>
    internal async Task CompleteRegistrationAsync(
        Session session,
        SteamLoginResult login,
        CancellationToken cancellationToken)
    {
        session.SetSteamLogin(login);
        session.Advance(SessionState.Authenticated, Deadline(options.SessionTtl));
        session.Advance(SessionState.Registering, Deadline(options.SessionTtl));

        var step = "device registration";

        try
        {
            var credentials = await steps.AcquireDeviceCredentialsAsync(cancellationToken).ConfigureAwait(false);

            // Pattern rather than string.IsNullOrEmpty so the compiler sees the non-null narrowing.
            if (credentials.ExpoPushToken is not { Length: > 0 })
            {
                // RCS1140 wants this documented on the method, but the exception never escapes it —
                // the catch block right below handles it. Suppressed rather than documented, since
                // documenting it would mislead a caller into thinking it can propagate.
#pragma warning disable RCS1140
                throw new InvalidOperationException("Device registration returned no Expo push token.");
#pragma warning restore RCS1140
            }

            step = "Rust Companion registration";
#pragma warning disable RCS1140 // Caught by the catch block below; see the comment above.
            var steamToken = session.SteamToken
                             ?? throw new InvalidOperationException("The session carries no Steam token.");
#pragma warning restore RCS1140

            await steps.RegisterWithCompanionAsync(steamToken, credentials.ExpoPushToken, cancellationToken)
                .ConfigureAwait(false);

            session.ClearSteamToken();
            session.SetCredentials(credentials);
            store.RecordCompletion(session.ClientIp);
            session.Advance(SessionState.Ready, Deadline(options.SessionTtl));

            session.Events.Publish(new SessionEvent("credentials", new CredentialsPayload(
                session.SteamId.ToString(CultureInfo.InvariantCulture),
                CredentialsStore.Serialize(credentials))));
        }
        catch (OperationCanceledException)
        {
            // The session was disposed or the host is shutting down. Nothing to report.
            session.ClearSteamToken();
        }
#pragma warning disable CA1031 // Any upstream failure must land the session in Failed rather than crash the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            session.ClearSteamToken();
            logger.LogFlowFailed(step, session.SessionId, ex);

            session.Advance(SessionState.Failed, Deadline(options.SessionTtl));
            session.Events.Publish(new SessionEvent("error", new ErrorPayload(
                $"Something went wrong during {step}. Start over — nothing was saved.")));
        }
    }

    /// <summary>Now plus <paramref name="ttl"/>, from the injected clock.</summary>
    /// <param name="ttl">How long the session should live from now.</param>
    private DateTimeOffset Deadline(TimeSpan ttl) => timeProvider.GetUtcNow().Add(ttl);
}
