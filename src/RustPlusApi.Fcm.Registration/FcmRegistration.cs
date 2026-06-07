using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Registration.Steps;

namespace RustPlusApi.Fcm.Registration;

/// <summary>
/// Orchestrates the native credential acquisition flow (v2 §7), replacing the Node CLI.
/// </summary>
/// <param name="httpClient">Optional <see cref="HttpClient"/> to use for all HTTP requests; a new instance is created if <see langword="null"/>.</param>
/// <param name="steamLoginPort">The loopback port the OAuth callback listener binds to.</param>
/// <remarks>
/// Every network step hits live Google / Expo / Facepunch services and is upstream-fragile;
/// it cannot be validated offline. See <see cref="RegistrationConstants"/>.
/// </remarks>
public sealed class FcmRegistration(HttpClient? httpClient = null, int steamLoginPort = 3000)
{
    private readonly AndroidFcmRegister _androidFcmRegister = new(httpClient);
    private readonly ExpoPushClient _expoPushClient = new(httpClient);
    private readonly RustCompanionClient _rustCompanionClient = new(httpClient);
    private readonly SteamLoginService _steamLoginService = new(steamLoginPort);

    /// <summary>
    /// Steps 1–4: GCM check-in, Firebase install, FCM register and Expo token. Returns the
    /// <see cref="Credentials"/> the FCM listener needs (GCM identity + FCM + Expo tokens).
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task<Credentials> AcquireCredentialsAsync(CancellationToken cancellationToken = default)
    {
        var (gcm, fcmToken) = await _androidFcmRegister.RegisterAsync(cancellationToken).ConfigureAwait(false);
        var expoToken = await _expoPushClient.GetTokenAsync(fcmToken, cancellationToken).ConfigureAwait(false);

        return new Credentials
        {
            Gcm = gcm,
            Fcm = new FcmToken { Token = fcmToken },
            ExpoPushToken = expoToken
        };
    }

    /// <summary>
    /// Steps 5–6: interactive Steam login, then register the device's Expo token with Rust
    /// Companion so it receives pairing pushes. Returns the captured Steam auth token.
    /// </summary>
    /// <param name="credentials">Credentials obtained from <see cref="AcquireCredentialsAsync"/>.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="credentials"/> is missing the Expo push token.</exception>
    public async Task<string> RegisterWithRustPlusAsync(Credentials credentials, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(credentials.ExpoPushToken))
            throw new InvalidOperationException("Credentials are missing the Expo push token; call AcquireCredentialsAsync first.");

        var steamToken = await _steamLoginService.LoginAsync(cancellationToken).ConfigureAwait(false);
        await _rustCompanionClient
            .RegisterAsync(steamToken, credentials.ExpoPushToken!, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return steamToken;
    }
}
