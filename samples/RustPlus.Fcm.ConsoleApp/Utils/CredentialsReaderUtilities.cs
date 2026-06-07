using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using RustPlusApi.Fcm.Data;

using System.Diagnostics.CodeAnalysis;

// ReSharper disable ClassNeverInstantiated.Global

namespace RustPlus.Fcm.ConsoleApp.Utils;

internal static class CredentialsReaderUtilities
{
    private static readonly JsonSerializerOptions NativeOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Loads credentials from either the native format written by
    /// <c>RustPlusApi.Fcm.Registration</c> (the recommended path) or the legacy
    /// <c>rustplus.js</c> config format.
    /// </summary>
    /// <param name="configFilePath">Path to the JSON credentials file.</param>
    /// <exception cref="InvalidOperationException">Thrown when the config file cannot be parsed as either format.</exception>
    public static Credentials LoadCredentials(this string configFilePath)
    {
        var json = File.ReadAllText(configFilePath);

        // Preferred: the native format produced by the Register sample / CredentialsStore.
        try
        {
            var native = JsonSerializer.Deserialize<Credentials>(json, NativeOptions);
            if (native?.Gcm is { AndroidId: not 0 }) return native;
        }
        catch (JsonException)
        {
            // Not the native format — fall through to the rustplus.js format.
        }

        // Fallback: the rustplus.js `fcm-register` output.
        var config = JsonSerializer.Deserialize<JavaScriptConfig>(json);
        if (config?.FcmCredentials?.Gcm == null)
            throw new InvalidOperationException("Invalid config file - missing FCM credentials");
        return config.ConvertToCredentials();
    }

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
                AndroidId = ulong.Parse(config.FcmCredentials.Gcm.AndroidId, CultureInfo.InvariantCulture),
                SecurityToken = ulong.Parse(config.FcmCredentials.Gcm.SecurityToken, CultureInfo.InvariantCulture)
            }
        };
    }
}

[SuppressMessage("Performance", "CA1812:AvoidUninstantiatedInternalClasses", Justification = "Instantiated by JSON deserialization.")]
internal sealed record JavaScriptConfig
{
    [JsonPropertyName("fcm_credentials")]
    public FcmCredentialsSection? FcmCredentials { get; init; }

    [JsonPropertyName("expo_push_token")]
    public string? ExpoPushToken { get; init; }
}

[SuppressMessage("Performance", "CA1812:AvoidUninstantiatedInternalClasses", Justification = "Instantiated by JSON deserialization.")]
internal sealed record FcmCredentialsSection
{
    [JsonPropertyName("gcm")]
    public GcmSection? Gcm { get; init; }

    [JsonPropertyName("fcm")]
    public FcmSection? Fcm { get; init; }
}

[SuppressMessage("Performance", "CA1812:AvoidUninstantiatedInternalClasses", Justification = "Instantiated by JSON deserialization.")]
internal sealed record GcmSection
{
    [JsonPropertyName("androidId")]
    public string AndroidId { get; init; } = null!;

    [JsonPropertyName("securityToken")]
    public string SecurityToken { get; init; } = null!;
}

[SuppressMessage("Performance", "CA1812:AvoidUninstantiatedInternalClasses", Justification = "Instantiated by JSON deserialization.")]
internal sealed record FcmSection
{
    [JsonPropertyName("token")]
    public string Token { get; init; } = null!;
}
