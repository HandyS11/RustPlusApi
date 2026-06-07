using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using ProtoBuf;

using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Registration.Protobuf;

namespace RustPlusApi.Fcm.Registration.Steps;

/// <summary>
/// Steps 1–3 of the credential flow: GCM check-in → Firebase installation (FIS) → FCM register.
/// Ported from <c>@liamcottle/push-receiver</c>.
/// </summary>
/// <param name="httpClient">Optional <see cref="HttpClient"/> to use for all HTTP requests; a new instance is created if <see langword="null"/>.</param>
/// <remarks>
/// Hits live Google endpoints; cannot be validated offline. See <see cref="RegistrationConstants"/>.
/// </remarks>
public sealed class AndroidFcmRegister(HttpClient? httpClient = null)
{
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient();

    /// <summary>Runs check-in + FIS + FCM register and returns the GCM identity and FCM token.</summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task<(Gcm Gcm, string FcmToken)> RegisterAsync(CancellationToken cancellationToken = default)
    {
        var gcm = await CheckInAsync(cancellationToken).ConfigureAwait(false);
        var fisToken = await InstallAsync(cancellationToken).ConfigureAwait(false);
        var fcmToken = await RegisterFcmAsync(gcm, fisToken, cancellationToken).ConfigureAwait(false);
        return (gcm, fcmToken);
    }

    /// <summary>Step 1 — GCM check-in (protobuf) to obtain the Android id + security token.</summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task<Gcm> CheckInAsync(CancellationToken cancellationToken = default)
    {
        var request = new AndroidCheckinRequest
        {
            UserSerialNumber = 0,
            Version = 3,
            Checkin = new AndroidCheckinProto
            {
                Type = AndroidCheckinProto.DeviceType.DeviceChromeBrowser,
                ChromeBuild = new ChromeBuildProto
                {
                    Platform = ChromeBuildProto.PlatformType.Mac,
                    ChromeVersion = RegistrationConstants.ChromeVersion,
                    Channel = ChromeBuildProto.ChannelType.Stable
                }
            }
        };

#pragma warning disable RCS1261 // MemoryStream.DisposeAsync is a no-op; await using not available in netstandard2.0
        using var ms = new MemoryStream();
#pragma warning restore RCS1261
        Serializer.Serialize(ms, request);

        using var content = new ByteArrayContent(ms.ToArray());
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-protobuf");

        using var httpResponse = await _httpClient
            .PostAsync(new Uri(RegistrationConstants.CheckinUrl), content, cancellationToken)
            .ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();

#if NET10_0_OR_GREATER
        var bytes = await httpResponse.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
#else
        var bytes = await httpResponse.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
#endif
        var response = Serializer.Deserialize<AndroidCheckinResponse>(new MemoryStream(bytes));

        return new Gcm
        {
            AndroidId = response.AndroidId ?? 0,
            SecurityToken = response.SecurityToken ?? 0
        };
    }

    /// <summary>Step 2 — Firebase installation, returning the installation auth token.</summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when the Firebase installation response does not contain an auth token.</exception>
    public async Task<string> InstallAsync(CancellationToken cancellationToken = default)
    {
        const string url = RegistrationConstants.FirebaseInstallationsUrl;

        var body = JsonSerializer.Serialize(new
        {
            fid = GenerateFirebaseId(),
            appId = RegistrationConstants.GmsAppId,
            authVersion = "FIS_v2",
            sdkVersion = "a:17.0.0"
        });

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        requestMessage.Headers.TryAddWithoutValidation("x-goog-api-key", RegistrationConstants.ApiKey);
        requestMessage.Headers.TryAddWithoutValidation("X-Android-Package", RegistrationConstants.AndroidPackageName);
        requestMessage.Headers.TryAddWithoutValidation("X-Android-Cert", RegistrationConstants.AndroidPackageCert);

        using var httpResponse = await _httpClient.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();

#if NET10_0_OR_GREATER
        var json = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
        var json = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("authToken").GetProperty("token").GetString()
               ?? throw new InvalidOperationException("Firebase installation response did not contain an auth token.");
    }

    /// <summary>Step 3 — FCM register (c2dm/register3), returning the FCM token.</summary>
    /// <param name="gcm">GCM identity obtained from <see cref="CheckInAsync"/>.</param>
    /// <param name="firebaseInstallationToken">Firebase installation auth token from <see cref="InstallAsync"/>.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when FCM registration fails after multiple attempts.</exception>
    public async Task<string> RegisterFcmAsync(Gcm gcm, string firebaseInstallationToken, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["device"] = gcm.AndroidId.ToString(CultureInfo.InvariantCulture),
            ["app"] = RegistrationConstants.AndroidPackageName,
            ["cert"] = RegistrationConstants.AndroidPackageCert,
            ["app_ver"] = "1",
            ["X-subtype"] = RegistrationConstants.GcmSenderId,
            ["X-app_ver"] = "1",
            ["X-osv"] = "29",
            ["X-cliv"] = "fiid-21.1.1",
            ["X-gmsv"] = "220217001",
            ["X-scope"] = "*",
            ["X-Goog-Firebase-Installations-Auth"] = firebaseInstallationToken,
            ["X-gms_app_id"] = RegistrationConstants.GmsAppId,
            ["X-Firebase-Client"] = "fire-abt/21.1.1 fire-installations/17.0.0 fire-android/ fire-core/20.3.1",
            ["X-firebase-app-name-hash"] = "R1dAH9Ui7M-ynoznwBdw01tLxhI",
            ["X-Goog-Firebase-Installations-Auth-Version"] = "FIS_v2",
            ["sender"] = RegistrationConstants.GcmSenderId,
            ["target_ver"] = "31",
        };

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var content = new FormUrlEncodedContent(form);
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, RegistrationConstants.FcmRegisterUrl)
            {
                Content = content
            };
            var androidId = gcm.AndroidId.ToString(CultureInfo.InvariantCulture);
            var securityToken = gcm.SecurityToken.ToString(CultureInfo.InvariantCulture);
            requestMessage.Headers.TryAddWithoutValidation("Authorization", $"AidLogin {androidId}:{securityToken}");

            using var httpResponse = await _httpClient.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);
#if NET10_0_OR_GREATER
            var responseText = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
            var responseText = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif

            if (!responseText.Contains("Error", StringComparison.Ordinal))
                return responseText.Split('=')[1];

            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException("FCM registration failed after multiple attempts.");
    }

    /// <summary>Generates a Firebase installation id: 17 random bytes with the first 4 bits set to 0111, base64url, unpadded.</summary>
    internal static string GenerateFirebaseId()
    {
        var buffer = new byte[17];
#if NET10_0_OR_GREATER
        RandomNumberGenerator.Fill(buffer);
#else
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(buffer);
#endif
        // Replace the first 4 bits with 0b0111 to make it a valid FID.
        buffer[0] = (byte)(0b0111_0000 | (buffer[0] & 0b0000_1111));

        return Convert.ToBase64String(buffer)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
