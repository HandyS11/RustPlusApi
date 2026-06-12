using System.Text.Json;

namespace RustPlus.Camera.ConsoleApp.Utils;

internal static class CredentialsReaderUtility
{
    public static Credentials GetConfig(this string configFilePath)
    {
        var configContent = File.ReadAllText(configFilePath);
        var config = JsonSerializer.Deserialize<Credentials>(configContent, JsonUtilities.JsonConfigOptions);

        if (config == null)
            throw new InvalidOperationException("Invalid config file - unable to deserialize");
        if (string.IsNullOrEmpty(config.Ip) ||
            config.Port <= 0 ||
            config.PlayerId <= 0 ||
            config.PlayerToken == 0)
        {
            throw new InvalidOperationException("Invalid config file - missing or invalid credentials");
        }

        return config;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:AvoidUninstantiatedInternalClasses",
        Justification = "Instantiated by JSON deserialization.")]
    internal sealed record Credentials
    {
        public required string Ip { get; init; }
        public int Port { get; init; }
        public ulong PlayerId { get; init; }
        public int PlayerToken { get; init; }
    }
}
