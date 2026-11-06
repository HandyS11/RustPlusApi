using RustPlusApi.Fcm.Registration.Steps.Browser;

namespace RustPlusApi.Fcm.Registration.UnitTests.TestHelpers;

/// <summary>An in-memory <see cref="IBrowserEnvironment"/>.</summary>
internal sealed class FakeBrowserEnvironment : IBrowserEnvironment
{
    public HashSet<string> Files { get; } = [];

    public HashSet<string> Directories { get; } = [];

    public Dictionary<string, string> Variables { get; } = [];

    public Dictionary<Environment.SpecialFolder, string> Folders { get; } = [];

    /// <summary>The text <see cref="ReadFileHead"/> returns per path; a missing path reads as unreadable.</summary>
    public Dictionary<string, string> FileHeads { get; } = [];

    /// <summary>Every <see cref="ReadFileHead"/> call, in order.</summary>
    public List<(string Path, int MaxBytes)> HeadReads { get; } = [];

    public bool IsWindows { get; set; }

    public bool IsMacOS { get; set; }

    public string HomeDirectory { get; set; } = "/home/u";

    public string TempDirectory { get; set; } = "/tmp";

    public string? GetEnvironmentVariable(string name) => Variables.TryGetValue(name, out var v) ? v : null;

    public string GetFolderPath(Environment.SpecialFolder folder) =>
        Folders.TryGetValue(folder, out var v) ? v : string.Empty;

    public bool FileExists(string path) => Files.Contains(path);

    public bool DirectoryExists(string path) => Directories.Contains(path);

    public string? ReadFileHead(string path, int maxBytes)
    {
        HeadReads.Add((path, maxBytes));
        return FileHeads.TryGetValue(path, out var head) ? head : null;
    }
}
