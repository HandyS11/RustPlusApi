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

    /// <summary>Serializes <paramref name="credentials"/> to an indented JSON string.</summary>
    /// <param name="credentials">The credentials to serialize.</param>
    public static string Serialize(Credentials credentials) =>
        JsonSerializer.Serialize(credentials, Options);

    /// <summary>Deserializes <paramref name="json"/> back into a <see cref="Credentials"/> instance.</summary>
    /// <param name="json">The JSON string produced by <see cref="Serialize"/>.</param>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="json"/> cannot be deserialized.</exception>
    public static Credentials Deserialize(string json) =>
        JsonSerializer.Deserialize<Credentials>(json)
        ?? throw new InvalidOperationException("Could not deserialize credentials.");

    /// <summary>Serializes <paramref name="credentials"/> and writes the result to <paramref name="path"/>.</summary>
    /// <param name="path">The file path to write to.</param>
    /// <param name="credentials">The credentials to persist.</param>
    public static void Save(string path, Credentials credentials) =>
        File.WriteAllText(path, Serialize(credentials));

    /// <summary>Reads the file at <paramref name="path"/> and deserializes it into a <see cref="Credentials"/> instance.</summary>
    /// <param name="path">The file path previously written by <see cref="Save"/>.</param>
    public static Credentials Load(string path) =>
        Deserialize(File.ReadAllText(path));
}
