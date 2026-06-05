using System.Text.Json;

using RustPlusApi.Fcm.Data;

namespace RustPlusApi.Fcm.Registration;

/// <summary>
/// Persists <see cref="Credentials"/> to JSON (the <c>rustplus.config.json</c> equivalent),
/// so they survive between the one-time registration and later <c>RustPlusFcm</c> runs.
/// </summary>
public static class CredentialsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Serialize(Credentials credentials) =>
        JsonSerializer.Serialize(credentials, Options);

    public static Credentials Deserialize(string json) =>
        JsonSerializer.Deserialize<Credentials>(json)
        ?? throw new InvalidOperationException("Could not deserialize credentials.");

    public static void Save(string path, Credentials credentials) =>
        File.WriteAllText(path, Serialize(credentials));

    public static Credentials Load(string path) =>
        Deserialize(File.ReadAllText(path));
}
