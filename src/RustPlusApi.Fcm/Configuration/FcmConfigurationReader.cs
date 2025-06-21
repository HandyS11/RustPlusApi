using Newtonsoft.Json;
using RustPlusApi.Fcm.Data;

namespace RustPlusApi.Fcm.Configuration;

public static class FcmConfigurationReader
{
    public static async Task<Credentials> ReadAndRegisterFromJavaScriptConfig(string configFilePath)
    {
        var configContent = File.ReadAllText(configFilePath);
        var config = JsonConvert.DeserializeObject<JavaScriptConfig>(configContent);

        if (config?.FcmCredentials?.Gcm == null)
            throw new InvalidOperationException("Invalid JavaScript config file - missing FCM credentials");

        if (string.IsNullOrEmpty(config.ExpoPushToken) || string.IsNullOrEmpty(config.RustplusAuthToken))
            throw new InvalidOperationException("Missing expo_push_token or rustplus_auth_token - run JavaScript fcm-register first");

        // Register with Rust+ API to actually receive notifications!
        Console.WriteLine("Registering with Rust+ API...");
        await RegisterWithRustPlusApi(config.RustplusAuthToken, config.ExpoPushToken);
        Console.WriteLine("✅ Successfully registered with Rust+ API");

        return new Credentials
        {
            // Dummy crypto keys - not used for Rust+ notifications
            Keys = new Keys
            {
                PrivateKey = "dummy-not-used",
                PublicKey = "dummy-not-used",
                AuthSecret = "dummy-not-used"
            },
            Gcm = new Gcm
            {
                AndroidId = ulong.Parse(config.FcmCredentials.Gcm.AndroidId),
                SecurityToken = ulong.Parse(config.FcmCredentials.Gcm.SecurityToken)
            }
        };
    }

    private static async Task RegisterWithRustPlusApi(string authToken, string expoPushToken)
    {
        using var httpClient = new HttpClient();
        var requestBody = new
        {
            AuthToken = authToken,
            DeviceId = "rustplus.js", // EXACT same as JavaScript - we're faking the same device
            PushKind = 3,
            PushToken = expoPushToken
        };

        var json = JsonConvert.SerializeObject(requestBody);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync("https://companion-rust.facepunch.com:443/api/push/register", content);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Failed to register with Rust+ API: {response.StatusCode} - {errorContent}");
        }
    }

    public static Credentials ReadFromJavaScriptConfig(string configFilePath)
    {
        var configContent = File.ReadAllText(configFilePath);
        var config = JsonConvert.DeserializeObject<JavaScriptConfig>(configContent);

        if (config?.FcmCredentials?.Gcm == null)
            throw new InvalidOperationException("Invalid JavaScript config file - missing FCM credentials");

        return new Credentials
        {
            // Dummy crypto keys - not used for Rust+ notifications
            Keys = new Keys
            {
                PrivateKey = "dummy-not-used",
                PublicKey = "dummy-not-used",
                AuthSecret = "dummy-not-used"
            },
            Gcm = new Gcm
            {
                AndroidId = ulong.Parse(config.FcmCredentials.Gcm.AndroidId),
                SecurityToken = ulong.Parse(config.FcmCredentials.Gcm.SecurityToken)
            }
        };
    }
}

public class JavaScriptConfig
{
    [JsonProperty("fcm_credentials")]
    public FcmCredentialsSection? FcmCredentials { get; set; }

    [JsonProperty("expo_push_token")]
    public string? ExpoPushToken { get; set; }

    [JsonProperty("rustplus_auth_token")]
    public string? RustplusAuthToken { get; set; }
}

public class FcmCredentialsSection
{
    [JsonProperty("gcm")]
    public GcmSection? Gcm { get; set; }

    [JsonProperty("fcm")]
    public FcmSection? Fcm { get; set; }
}

public class GcmSection
{
    [JsonProperty("androidId")]
    public string AndroidId { get; set; } = null!;

    [JsonProperty("securityToken")]
    public string SecurityToken { get; set; } = null!;
}

public class FcmSection
{
    [JsonProperty("token")]
    public string Token { get; set; } = null!;
}
