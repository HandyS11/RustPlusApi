using RustPlusApi.Fcm.Data;
using System.Text.Json;

namespace RustPlusApi.Fcm.Registration;

/// <summary>
/// Persists <see cref="Credentials"/> to JSON (the <c>rustplus.config.json</c> equivalent),
/// so they survive between the one-time registration and later <c>RustPlusFcm</c> runs.
/// </summary>
public static class CredentialsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

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

    /// <summary>Serializes <paramref name="credentials"/> and writes the result to <paramref name="path"/>.
    /// The file contains long-lived push credentials (GCM security token, FCM/Expo tokens) in plain
    /// JSON — treat it like a password file. On .NET 10+ on Unix it is restricted to owner read/write.</summary>
    /// <param name="path">The file path to write to.</param>
    /// <param name="credentials">The credentials to persist.</param>
    public static void Save(string path, Credentials credentials)
    {
#if NET10_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
        {
            // Create the file with the restrictive mode from the first byte, so it is never
            // observable with the umask default. The create mode only applies to new files —
            // overwriting keeps the existing inode's mode — so tighten it afterwards as well.
            const UnixFileMode ownerReadWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(path, new FileStreamOptions
                   {
                       Mode = FileMode.Create, Access = FileAccess.Write, UnixCreateMode = ownerReadWrite,
                   }))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(Serialize(credentials));
            }

            File.SetUnixFileMode(path, ownerReadWrite);
            return;
        }
#endif
        File.WriteAllText(path, Serialize(credentials));
    }

    /// <summary>Reads the file at <paramref name="path"/> and deserializes it into a <see cref="Credentials"/> instance.</summary>
    /// <param name="path">The file path previously written by <see cref="Save"/>.</param>
    public static Credentials Load(string path) =>
        Deserialize(File.ReadAllText(path));
}
