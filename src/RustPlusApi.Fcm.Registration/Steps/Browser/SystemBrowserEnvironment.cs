using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace RustPlusApi.Fcm.Registration.Steps.Browser;

/// <summary>The real operating system behind <see cref="IBrowserEnvironment"/>.</summary>
internal sealed class SystemBrowserEnvironment : IBrowserEnvironment
{
    /// <summary>The shared instance.</summary>
    public static readonly SystemBrowserEnvironment Instance = new();

    private SystemBrowserEnvironment()
    {
    }

    /// <inheritdoc />
    public bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <inheritdoc />
    public bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <inheritdoc />
    public string HomeDirectory => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <inheritdoc />
    [SuppressMessage("Security", "S5443:Publicly writable directories should not be used",
        Justification =
            "Only the parent of a per-login profile directory, which is created with a random name and deleted afterwards.")]
    public string TempDirectory => Path.GetTempPath();

    /// <inheritdoc />
    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    /// <inheritdoc />
    public string GetFolderPath(Environment.SpecialFolder folder) => Environment.GetFolderPath(folder);

    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <inheritdoc />
    public string? ReadFileHead(string path, int maxBytes)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[maxBytes];
            // One read is enough for a heuristic: a local file returns everything up to maxBytes at once.
            var read = stream.Read(buffer, 0, maxBytes);
            return Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Missing, a directory, or not ours to read: not a launcher script we can inspect.
            return null;
        }
    }
}
