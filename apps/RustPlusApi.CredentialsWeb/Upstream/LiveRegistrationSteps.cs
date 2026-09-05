using Microsoft.Extensions.Http;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Registration;
using RustPlusApi.Fcm.Registration.Steps;
using System.Diagnostics.CodeAnalysis;

namespace RustPlusApi.CredentialsWeb.Upstream;

/// <summary>The real implementation, delegating to RustPlusApi.Fcm.Registration.</summary>
/// <param name="httpClientFactory">Source of clients. A factory rather than a captured
/// <see cref="HttpClient"/> because this type is a singleton, and a singleton-held client never
/// picks up DNS changes.</param>
/// <param name="loggerFactory">Passed to <see cref="PairingListener"/> so its skip paths are visible.</param>
[ExcludeFromCodeCoverage(Justification =
    "Live-network seam: every member drives Google, Expo, Facepunch or the MCS socket and cannot be "
    + "validated offline. All logic above it is tested against IRegistrationSteps.")]
internal sealed class LiveRegistrationSteps(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : IRegistrationSteps
{
    /// <summary>Named client used for every upstream call.</summary>
    internal const string HttpClientName = "upstream";

    /// <inheritdoc/>
    public async Task<Credentials> AcquireDeviceCredentialsAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var (gcm, fcmToken) = await new AndroidFcmRegister(client)
            .RegisterAsync(cancellationToken).ConfigureAwait(false);
        var expoToken = await new ExpoPushClient(client)
            .GetTokenAsync(fcmToken, cancellationToken).ConfigureAwait(false);

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

    /// <inheritdoc/>
    public Task RegisterWithCompanionAsync(
        string steamToken,
        string expoPushToken,
        CancellationToken cancellationToken) =>
        new RustCompanionClient(httpClientFactory.CreateClient(HttpClientName))
            .RegisterAsync(steamToken, expoPushToken, cancellationToken: cancellationToken);

    /// <inheritdoc/>
    public async Task<ServerPairing> WaitForPairingAsync(
        Credentials credentials,
        CancellationToken cancellationToken)
    {
        using var listener = new PairingListener(
            credentials,
            loggerFactory: loggerFactory,
            httpClient: httpClientFactory.CreateClient(HttpClientName));

        return await listener.WaitForServerPairingAsync(cancellationToken).ConfigureAwait(false);
    }
}
