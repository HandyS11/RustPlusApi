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

    /// <summary>Step 6: hold an MCS socket until a pairing push arrives, the TTL runs out, or the
    /// session is disposed. The caller must already hold a pairing slot; this method always
    /// releases it.</summary>
    /// <param name="session">The session to pair. Refused unless the claim below finds it still in
    /// <see cref="SessionState.Ready"/> with credentials to pair against.</param>
    /// <param name="cancellationToken">Token to abandon the wait.</param>
    internal async Task WaitForPairingAsync(Session session, CancellationToken cancellationToken)
    {
        // One atomic claim rather than a Ready check followed by a separate Advance: the endpoint
        // dispatches this against a state it read a moment earlier, so two concurrent pairing POSTs
        // for one session can both arrive here believing it is Ready. Session.TryClaimForPairing
        // settles that under the session's own lock — the loser starts nothing and hands its slot
        // straight back — and returns the credentials it read there, so a racing disposal cannot
        // null them between the check and their use.
        //
        // The expiry it commits is the *session's* TTL, not the pairing wait's — a pairing timeout
        // must not make the sweeper reap the session out from under it (that would also cancel
        // session.Lifetime, the only thing the Advance calls below could then no-op against). The
        // wait gets its own deadline instead: a TimeProvider-driven source (so FakeTimeProvider-
        // driven tests can still fire it deterministically) linked to the caller's token so
        // disposal still cuts it short.
        if (!session.TryClaimForPairing(Deadline(options.SessionTtl), out var credentials))
        {
            store.ReleasePairingSlot();
            return;
        }

        try
        {
            // Constructed inside the try, not before it: new CancellationTokenSource(delay, ...)
            // throws ArgumentOutOfRangeException for a delay above ~49.7 days, and a misconfigured
            // PairingTtl is only guarded against being zero or negative (AppOptionsValidator), not
            // against being unreasonably large. Building these here — rather than between the
            // Advance above and this try, where such a throw would escape past the finally below —
            // keeps "this method always releases the pairing slot" true unconditionally instead of
            // depending on validation staying in sync with CancellationTokenSource's own limits.
            using var pairingDeadline = new CancellationTokenSource(options.PairingTtl, timeProvider);
            using var linkedToken =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, pairingDeadline.Token);

            var pairing = await steps.WaitForPairingAsync(credentials, linkedToken.Token).ConfigureAwait(false);

            session.SetPairing(pairing);
            session.Advance(SessionState.Paired, Deadline(options.SessionTtl));
            session.Events.Publish(new SessionEvent("paired", new PairedPayload(
                pairing.Ip,
                pairing.Port,
                pairing.PlayerId.ToString(CultureInfo.InvariantCulture),
                pairing.PlayerToken,
                pairing.Name)));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The pairing wait's own deadline elapsed — the caller's token is still live, so this
            // is a genuine timeout, not a disposal. Back to Ready, not a terminal state: the
            // credentials are still good, so the visitor can start another pairing wait without
            // repeating the Steam login.
            session.Advance(SessionState.Ready, Deadline(options.SessionTtl));
            session.Events.Publish(new SessionEvent("expired", null));
        }
        catch (OperationCanceledException)
        {
            // The session was disposed or the host is shutting down. Nothing to report.
        }
#pragma warning disable CA1031 // A failed socket must land the session in Failed rather than crash the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogPairingFailed(session.SessionId, ex);
            session.Advance(SessionState.Failed, Deadline(options.SessionTtl));
            session.Events.Publish(new SessionEvent("error", new ErrorPayload(
                "The pairing listener stopped unexpectedly. Your credentials are still valid — "
                + "try the pairing step again.")));
        }
        finally
        {
            // A leaked slot permanently shrinks this instance's pairing capacity and nothing reports it.
            store.ReleasePairingSlot();
        }
    }

    /// <summary>Now plus <paramref name="ttl"/>, from the injected clock.</summary>
    /// <param name="ttl">How long the session should live from now.</param>
    private DateTimeOffset Deadline(TimeSpan ttl) => timeProvider.GetUtcNow().Add(ttl);
}
