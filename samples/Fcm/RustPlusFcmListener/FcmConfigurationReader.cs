using System.Text.Json;
using System.Text.Json.Serialization;
using RustPlusApi.Fcm.Data;

// ReSharper disable ClassNeverInstantiated.Global

namespace RustPlusFcmListener;

public static class FcmConfigurationReader
{
    public static JavaScriptConfig ReadJavaScriptConfig(string configFilePath)
    {
        var configContent = File.ReadAllText(configFilePath);
        var config = JsonSerializer.Deserialize<JavaScriptConfig>(configContent);

        if (config?.FcmCredentials?.Gcm == null)
            throw new InvalidOperationException("Invalid JavaScript config file - missing FCM credentials");

        return config;
    }

    public static Credentials ConvertToCredentials(JavaScriptConfig config)
    {
        if (config.FcmCredentials?.Gcm == null)
            throw new InvalidOperationException("Invalid config - missing FCM credentials");

        return new Credentials
        {
            Gcm = new Gcm
            {
                AndroidId = ulong.Parse(config.FcmCredentials.Gcm.AndroidId),
                SecurityToken = ulong.Parse(config.FcmCredentials.Gcm.SecurityToken)
            }
        };
    }
}

public sealed record JavaScriptConfig
{
    [JsonPropertyName("fcm_credentials")]
    public FcmCredentialsSection? FcmCredentials { get; set; }

    [JsonPropertyName("expo_push_token")]
    public string? ExpoPushToken { get; set; }

    [JsonPropertyName("rustplus_auth_token")]
    public string? RustplusAuthToken { get; set; }
}

public sealed record FcmCredentialsSection
{
    [JsonPropertyName("gcm")]
    public GcmSection? Gcm { get; set; }

    [JsonPropertyName("fcm")]
    public FcmSection? Fcm { get; set; }
}

public sealed record GcmSection
{
    [JsonPropertyName("androidId")]
    public string AndroidId { get; set; } = null!;

    [JsonPropertyName("securityToken")]
    public string SecurityToken { get; set; } = null!;
}

public sealed record FcmSection
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = null!;
}
