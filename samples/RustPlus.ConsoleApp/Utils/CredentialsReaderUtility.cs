using System.Text.Json;

namespace RustPlus.ConsoleApp.Utils;

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

        if (config.ServerId is not null && string.IsNullOrWhiteSpace(config.AuthToken))
        {
            throw new InvalidOperationException(
                "Invalid config file - authToken is required when serverId is set (the companion socket needs it)");
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

        /// <summary>Facepunch's server id from the pairing; set it (with <see cref="AuthToken"/>) to
        /// connect through the companion socket instead of <see cref="Ip"/>:<see cref="Port"/>.</summary>
        public Guid? ServerId { get; init; }

        /// <summary>The Rust+ auth token from the Steam login — required with <see cref="ServerId"/>.</summary>
        public string? AuthToken { get; init; }

        /// <summary>Where <see cref="ToConnection"/> connects, for error messages.</summary>
        public string Target =>
            ServerId is { } serverId ? $"the companion socket for server {serverId}" : $"{Ip}:{Port}";

        public RustPlusApi.RustPlusConnection ToConnection() =>
            ServerId is { } serverId
                ? new RustPlusApi.CompanionConnection(serverId, AuthToken!, PlayerId, PlayerToken)
                : new RustPlusApi.DirectConnection(Ip, Port, PlayerId, PlayerToken);
    }
}
