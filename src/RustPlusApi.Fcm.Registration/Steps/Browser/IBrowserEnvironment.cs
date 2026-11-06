namespace RustPlusApi.Fcm.Registration.Steps.Browser;

/// <summary>The slice of the operating system that browser lookup and launch need, as a test seam.</summary>
internal interface IBrowserEnvironment
{
    /// <summary>Whether the process runs on Windows.</summary>
    bool IsWindows { get; }

    /// <summary>Whether the process runs on macOS.</summary>
    bool IsMacOS { get; }

    /// <summary>The current user's home directory.</summary>
    string HomeDirectory { get; }

    /// <summary>The temporary directory.</summary>
    string TempDirectory { get; }

    /// <summary>Reads an environment variable; <see langword="null"/> when unset.</summary>
    /// <param name="name">The variable name.</param>
    string? GetEnvironmentVariable(string name);

    /// <summary>Resolves a well-known folder.</summary>
    /// <param name="folder">The folder to resolve.</param>
    string GetFolderPath(Environment.SpecialFolder folder);

    /// <summary>Whether a file exists.</summary>
    /// <param name="path">The path to test.</param>
    bool FileExists(string path);

    /// <summary>Whether a directory exists.</summary>
    /// <param name="path">The path to test.</param>
    bool DirectoryExists(string path);

    /// <summary>Reads the start of a file as UTF-8 text, to tell a launcher script from a program.</summary>
    /// <param name="path">The file to read.</param>
    /// <param name="maxBytes">The most bytes to read.</param>
    /// <returns>The text read, or <see langword="null"/> when the file cannot be read.</returns>
    string? ReadFileHead(string path, int maxBytes);
}
