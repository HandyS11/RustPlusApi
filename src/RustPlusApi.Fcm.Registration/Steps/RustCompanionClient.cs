using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace RustPlusApi.Fcm.Registration.Steps;

/// <summary>Step 6 — registers the device (its Expo push token) with the Rust Companion API.</summary>
/// <param name="httpClient">Optional <see cref="HttpClient"/> to use; a new instance is created if <see langword="null"/>.</param>
/// <remarks>Hits the live Facepunch service; cannot be validated offline.</remarks>
public sealed class RustCompanionClient(HttpClient? httpClient = null)
{
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient();

    /// <summary>
    /// Subscribes the device to pairing pushes.
    /// </summary>
    /// <param name="steamAuthToken">The Steam auth token from <see cref="SteamLoginService"/>.</param>
    /// <param name="expoPushToken">The Expo push token from <see cref="ExpoPushClient"/>.</param>
    /// <param name="deviceId">An arbitrary device identifier; defaults to the library name.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task RegisterAsync(
        string steamAuthToken,
        string expoPushToken,
        string deviceId = "RustPlusApi",
        CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            AuthToken = steamAuthToken, DeviceId = deviceId, PushKind = 3, PushToken = expoPushToken
        });

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var httpResponse = await _httpClient
            .PostAsync(new Uri(RegistrationConstants.CompanionRegisterUrl), content, cancellationToken)
            .ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();
    }
}
