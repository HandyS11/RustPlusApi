using Newtonsoft.Json;
using RustPlusApi.Fcm.Data;

namespace RustPlusApi.Fcm.Configuration;

public static class FcmConfigurationReader
{
    public static JavaScriptConfig ReadJavaScriptConfig(string configFilePath)
    {
        var configContent = File.ReadAllText(configFilePath);
        var config = JsonConvert.DeserializeObject<JavaScriptConfig>(configContent);

        if (config?.FcmCredentials?.Gcm == null)
            throw new InvalidOperationException("Invalid JavaScript config file - missing FCM credentials");

        return config;
    }

    public static Credentials ConvertToCredentials(JavaScriptConfig config)
    {
        if (config?.FcmCredentials?.Gcm == null)
            throw new InvalidOperationException("Invalid config - missing FCM credentials");

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
