using System.Text.Json;
using System.Text.Json.Serialization;
using RustPlusApi.Fcm.Data;

// ReSharper disable ClassNeverInstantiated.Global

namespace RustPlus.Fcm.ConsoleApp.Utils;

public static class CredentialsReaderUtilities
{
    public static JavaScriptConfig ReadJavaScriptConfig(this string configFilePath)
    {
        var configContent = File.ReadAllText(configFilePath);
        var config = JsonSerializer.Deserialize<JavaScriptConfig>(configContent);

        if (config?.FcmCredentials?.Gcm == null)
            throw new InvalidOperationException("Invalid JavaScript config file - missing FCM credentials");

        return config;
    }

    public static Credentials ConvertToCredentials(this JavaScriptConfig config)
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
    public FcmCredentialsSection? FcmCredentials { get; init; }

    [JsonPropertyName("expo_push_token")]
    public string? ExpoPushToken { get; init; }

    [JsonPropertyName("rustplus_auth_token")]
    public string? RustplusAuthToken { get; init; }
}

public sealed record FcmCredentialsSection
{
    [JsonPropertyName("gcm")]
    public GcmSection? Gcm { get; init; }

    [JsonPropertyName("fcm")]
    public FcmSection? Fcm { get; init; }
}

public sealed record GcmSection
{
    [JsonPropertyName("androidId")]
    public string AndroidId { get; init; } = null!;

    [JsonPropertyName("securityToken")]
    public string SecurityToken { get; init; } = null!;
}

public sealed record FcmSection
{
    [JsonPropertyName("token")]
    public string Token { get; init; } = null!;
}