namespace RustPlusApi.Fcm.Registration.Steps.Browser;

/// <summary>
/// A temporary browser profile directory under a library-owned root. It holds an exclusive lock file for as long as the
/// login is running, so a later <see cref="SweepStale(string)"/> never deletes a profile another login is using.
/// </summary>
internal sealed class LoginProfile : IDisposable
{
    /// <summary>The name of the lock file kept open inside every profile directory.</summary>
    internal const string LockFileName = "rustplusapi-login.lock";

    /// <summary>How old a lock-less directory must be before a sweep treats it as abandoned.</summary>
    internal static readonly TimeSpan StaleLocklessAge = TimeSpan.FromMinutes(5);

    private readonly Action<string> _deleteDirectory;
    private FileStream? _lock;

    private LoginProfile(string directoryPath, FileStream lockStream, Action<string> deleteDirectory)
    {
        DirectoryPath = directoryPath;
        _lock = lockStream;
        _deleteDirectory = deleteDirectory;
    }

    /// <summary>Gets the profile directory.</summary>
    internal string DirectoryPath { get; }

    /// <summary>Releases the lock only; the directory is left for <see cref="TryDeleteAsync"/> or a later sweep.</summary>
    public void Dispose()
    {
        _lock?.Dispose();
        _lock = null;
    }

    /// <summary>Creates a fresh profile directory under <paramref name="root"/> and locks it.</summary>
    /// <param name="root">The directory that holds every login profile.</param>
    internal static LoginProfile Create(string root) => Create(root, DeleteRecursive);

    /// <summary>Creates a fresh profile directory with a custom directory deleter (a test seam).</summary>
    /// <param name="root">The directory that holds every login profile.</param>
    /// <param name="deleteDirectory">Deletes a directory tree.</param>
    internal static LoginProfile Create(string root, Action<string> deleteDirectory)
    {
        var path = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        var stream = new FileStream(
            Path.Combine(path, LockFileName), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        return new LoginProfile(path, stream, deleteDirectory);
    }

    /// <summary>
    /// Deletes profiles under <paramref name="root"/> that no live login holds. A directory with a lock file is deleted
    /// only if the lock can be taken exclusively. A lock-less directory is deleted only once it is older than
    /// <see cref="StaleLocklessAge"/>, because <see cref="Create(string)"/> makes the directory before it opens the
    /// lock, so a young lock-less directory may belong to a login that is starting right now.
    /// </summary>
    /// <param name="root">The directory that holds every login profile.</param>
    /// <returns>The number of profiles deleted.</returns>
    internal static int SweepStale(string root) => SweepStale(root, DeleteRecursive);

    /// <summary>Sweeps with a custom directory deleter (a test seam).</summary>
    /// <param name="root">The directory that holds every login profile.</param>
    /// <param name="deleteDirectory">Deletes a directory tree.</param>
    /// <returns>The number of profiles deleted.</returns>
    internal static int SweepStale(string root, Action<string> deleteDirectory)
    {
        if (!Directory.Exists(root))
        {
            return 0;
        }

        var deleted = 0;
        foreach (var directory in Directory.GetDirectories(root))
        {
            try
            {
                var lockPath = Path.Combine(directory, LockFileName);
                if (File.Exists(lockPath))
                {
                    // Holding the lock exclusively proves no live login owns this profile.
                    using var probe = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                }
                else if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(directory) < StaleLocklessAge)
                {
                    continue;
                }

                deleteDirectory(directory);
                deleted++;
            }
            catch (IOException)
            {
                // Locked by a live login, or still in use: leave it for a later sweep.
            }
            catch (UnauthorizedAccessException)
            {
                // Not ours to delete right now: leave it for a later sweep.
            }
        }

        return deleted;
    }

    /// <summary>Releases the lock and deletes the profile, retrying while the browser's files are still held.</summary>
    /// <param name="attempts">The maximum number of delete attempts.</param>
    /// <param name="delay">The wait between attempts.</param>
    /// <returns><see langword="true"/> when the directory is gone.</returns>
    internal async Task<bool> TryDeleteAsync(int attempts, TimeSpan delay)
    {
        Dispose();
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                if (Directory.Exists(DirectoryPath))
                {
                    _deleteDirectory(DirectoryPath);
                }

                return true;
            }
            catch (IOException)
            {
                // The browser may still be releasing its files.
            }
            catch (UnauthorizedAccessException)
            {
                // Same: a transient sharing violation surfaces this way on Windows.
            }

            if (attempt < attempts)
            {
                await Task.Delay(delay).ConfigureAwait(false);
            }
        }

        return false;
    }

    private static void DeleteRecursive(string path) => Directory.Delete(path, recursive: true);
}
