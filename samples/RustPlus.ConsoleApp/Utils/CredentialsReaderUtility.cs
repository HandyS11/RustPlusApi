using System.Text.Json;

namespace RustPlus.ConsoleApp.Utils;

public static class CredentialsReaderUtility
{
    public static Credentials GetConfig(this string configFilePath)
    {
        var configContent = File.ReadAllText(configFilePath);
        var config = JsonSerializer.Deserialize<Credentials>(configContent, JsonUtilities.JsonConfigOptions);

        if (config == null) throw new InvalidOperationException("Invalid config file - unable to deserialize");
        if (config.Ip == "" ||
            config.Port <= 0 ||
            config.PlayerId <= 0 ||
            config.PlayerToken == 0)
            throw new InvalidOperationException("Invalid config file - missing or invalid credentials");

        return config;
    }
    
    public record Credentials
    {
        public required string Ip { get; init; }
        public int Port { get; init; }
        public ulong PlayerId { get; init; }
        public int PlayerToken { get; init; }
    }
}