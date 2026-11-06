using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Registration.Steps;
using System.Diagnostics.CodeAnalysis;

namespace RustPlusApi.Fcm.Registration;

/// <summary>
/// Orchestrates the native credential acquisition flow, replacing the Node CLI.
/// </summary>
/// <param name="httpClient">Optional <see cref="HttpClient"/> to use for all HTTP requests; a new instance is created if <see langword="null"/>.</param>
/// <param name="steamLoginPort">Kept so existing callers keep compiling; no longer used. The Steam login runs in a
/// browser window the library controls and picks its own debugging port.</param>
/// <remarks>
/// Every network step hits live Google / Expo / Facepunch services and is upstream-fragile;
/// it cannot be validated offline. See <see cref="RegistrationConstants"/>.
/// </remarks>
#pragma warning disable CS9113 // 'steamLoginPort' is kept for source compatibility with existing callers; the Steam login no longer listens on a port.
public sealed class FcmRegistration(HttpClient? httpClient = null, int steamLoginPort = 3000)
#pragma warning restore CS9113
{
    private readonly AndroidFcmRegister _androidFcmRegister = new(httpClient);
    private readonly ExpoPushClient _expoPushClient = new(httpClient);
    private readonly RustCompanionClient _rustCompanionClient = new(httpClient);

    /// <summary>Gets the optional inputs for the Steam login in <see cref="RegisterWithRustPlusAsync"/>: a paste
    /// callback for when no browser can be driven, and an explicit browser path.</summary>
    public SteamLoginOptions SteamLoginOptions { get; init; } = new();

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
            Fcm = new FcmToken
            {
                Token = fcmToken
            },
            ExpoPushToken = expoToken
        };
    }

    /// <summary>
    /// Steps 5–6: interactive Steam login, then register the device's Expo token with Rust
    /// Companion so it receives pairing pushes. Returns the captured Steam identity.
    /// </summary>
    /// <param name="credentials">Credentials obtained from <see cref="AcquireCredentialsAsync"/>.</param>
    /// <param name="onLoginUrl">Invoked with the Steam login URL first, before the browser window is opened, so
    /// callers can print it. On a host where no browser can be driven, the user opens it by hand and pastes the
    /// result back through <see cref="SteamLoginOptions.ReadPastedAddress"/>.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="credentials"/> is missing the Expo push
    /// token, or when the Steam login fails (see <see cref="SteamLoginService.LoginAsync(Action{string}?, CancellationToken)"/>).</exception>
    /// <remarks>Excluded from coverage: past the guard it drives a real browser and the live Companion
    /// endpoint; the guard (missing ExpoPushToken) is unit-tested, and the Steam login orchestration is
    /// unit-tested over fakes in <see cref="SteamLoginService"/>.</remarks>
    [ExcludeFromCodeCoverage]
    public async Task<SteamLoginResult> RegisterWithRustPlusAsync(Credentials credentials,
        Action<string>? onLoginUrl = null,
        CancellationToken cancellationToken = default)
    {
        // Narrow with a pattern rather than string.IsNullOrEmpty: netstandard2.0's reference assembly lacks the
        // [NotNullWhen(false)] annotation, so only the pattern proves non-nullness to the compiler on both TFMs.
        var expoPushToken = credentials.ExpoPushToken;
        if (expoPushToken is not { Length: > 0 })
        {
            throw new InvalidOperationException(
                "Credentials are missing the Expo push token; call AcquireCredentialsAsync first.");
        }

        var steamLoginService = new SteamLoginService
        {
            Options = SteamLoginOptions
        };
        var steamLogin = await steamLoginService.LoginAsync(onLoginUrl, cancellationToken).ConfigureAwait(false);
        await _rustCompanionClient
            .RegisterAsync(steamLogin.Token, expoPushToken, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return steamLogin;
    }
}
