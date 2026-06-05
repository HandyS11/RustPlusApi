using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace RustPlusApi.Fcm.Registration;

/// <summary>Step 4 — exchanges the FCM token for an Expo push token (<c>ExponentPushToken[...]</c>).</summary>
/// <remarks>Hits the live Expo service; cannot be validated offline.</remarks>
public sealed class ExpoPushClient
{
    private readonly HttpClient _httpClient;

    public ExpoPushClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<string> GetTokenAsync(string fcmToken, CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            type = "fcm",
            deviceId = Guid.NewGuid().ToString(),
            development = false,
            appId = RegistrationConstants.AndroidPackageName,
            deviceToken = fcmToken,
            projectId = RegistrationConstants.ExpoProjectId
        });

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var httpResponse = await _httpClient
            .PostAsync(RegistrationConstants.ExpoPushTokenUrl, content, cancellationToken)
            .ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();

#if NET10_0_OR_GREATER
        var json = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
        var json = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("data").GetProperty("expoPushToken").GetString()
               ?? throw new InvalidOperationException("Expo response did not contain a push token.");
    }
}
