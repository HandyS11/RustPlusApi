using System.Net.Http;

using RustPlusApi.Fcm.Data;

namespace RustPlusApi.Fcm.Registration;

/// <summary>
/// Orchestrates the native credential acquisition flow (v2 §7), replacing the Node CLI.
/// </summary>
/// <remarks>
/// Every network step hits live Google / Expo / Facepunch services and is upstream-fragile;
/// it cannot be validated offline. See <see cref="RegistrationConstants"/>.
/// </remarks>
public sealed class FcmRegistration
{
    private readonly AndroidFcmRegister _androidFcmRegister;
    private readonly ExpoPushClient _expoPushClient;
    private readonly RustCompanionClient _rustCompanionClient;
    private readonly SteamLoginService _steamLoginService;

    public FcmRegistration(HttpClient? httpClient = null, int steamLoginPort = 3000)
    {
        _androidFcmRegister = new AndroidFcmRegister(httpClient);
        _expoPushClient = new ExpoPushClient(httpClient);
        _rustCompanionClient = new RustCompanionClient(httpClient);
        _steamLoginService = new SteamLoginService(steamLoginPort);
    }

    /// <summary>
    /// Steps 1–4: GCM check-in, Firebase install, FCM register and Expo token. Returns the
    /// <see cref="Credentials"/> the FCM listener needs (GCM identity + FCM + Expo tokens).
    /// </summary>
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
