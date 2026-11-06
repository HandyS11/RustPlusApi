using RustPlusApi.Fcm.Registration.Steps.Browser;
using Xunit;

namespace RustPlusApi.Fcm.Registration.UnitTests;

/// <summary>Pins the temporary browser profile lifecycle: creation, lock-guarded sweeping and retrying deletion.</summary>
public sealed class LoginProfileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "rustplusapi-loginprofile-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Create_MakesUniqueDirectoryUnderRootHoldingTheLock()
    {
        using var profile = LoginProfile.Create(_root);

        Assert.True(Directory.Exists(profile.DirectoryPath));
        Assert.Equal(_root, Path.GetDirectoryName(profile.DirectoryPath));
        var name = Path.GetFileName(profile.DirectoryPath);
        Assert.Equal(32, name.Length);
        Assert.All(name, c => Assert.True(Uri.IsHexDigit(c)));
        var lockPath = Path.Combine(profile.DirectoryPath, LoginProfile.LockFileName);
        Assert.True(File.Exists(lockPath));
        Assert.Throws<IOException>(() => new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
    }

    [Fact]
    public void SweepStale_SkipsProfileWhoseLockIsHeld()
    {
        using var profile = LoginProfile.Create(_root);

        Assert.Equal(0, LoginProfile.SweepStale(_root));
        Assert.True(Directory.Exists(profile.DirectoryPath));
    }

    [Fact]
    public void SweepStale_DeletesReleasedAndLockless()
    {
        var released = LoginProfile.Create(_root);
        var releasedPath = released.DirectoryPath;
        released.Dispose();
        var bare = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bare);
        Directory.SetLastWriteTimeUtc(bare, DateTime.UtcNow - TimeSpan.FromMinutes(10));

        Assert.Equal(2, LoginProfile.SweepStale(_root));
        Assert.False(Directory.Exists(releasedPath));
        Assert.False(Directory.Exists(bare));
    }

    [Fact]
    public void SweepStale_SkipsFreshLocklessDirectory()
    {
        var bare = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bare);

        Assert.Equal(0, LoginProfile.SweepStale(_root));
        Assert.True(Directory.Exists(bare));
    }

    [Fact]
    public void SweepStale_MissingRoot_ReturnsZero() => Assert.Equal(0, LoginProfile.SweepStale(_root));

    [Fact]
    public void SweepStale_DeleterDenied_SkipsAndDoesNotCount()
    {
        var released = LoginProfile.Create(_root);
        var path = released.DirectoryPath;
        released.Dispose();

        Assert.Equal(0, LoginProfile.SweepStale(_root, _ => throw new UnauthorizedAccessException("denied")));
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public async Task TryDeleteAsync_DeletesDirectoryAndReleasesLock()
    {
        using var profile = LoginProfile.Create(_root);

        Assert.True(await profile.TryDeleteAsync(3, TimeSpan.FromMilliseconds(1)));
        Assert.False(Directory.Exists(profile.DirectoryPath));
    }

    [Fact]
    public async Task TryDeleteAsync_AlreadyGone_ReturnsTrue()
    {
        using var profile = LoginProfile.Create(_root);
        Directory.Delete(profile.DirectoryPath, recursive: true);

        Assert.True(await profile.TryDeleteAsync(3, TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public async Task TryDeleteAsync_DeleterKeepsFailing_ReturnsFalse()
    {
        var calls = 0;
        using var profile = LoginProfile.Create(_root, _ =>
        {
            calls++;
            throw new IOException("locked");
        });

        Assert.False(await profile.TryDeleteAsync(3, TimeSpan.FromMilliseconds(1)));
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task TryDeleteAsync_UnauthorizedOnce_RetriesThenSucceeds()
    {
        var calls = 0;
        using var profile = LoginProfile.Create(_root, _ =>
        {
            if (++calls == 1)
            {
                throw new UnauthorizedAccessException("denied");
            }
        });

        Assert.True(await profile.TryDeleteAsync(3, TimeSpan.FromMilliseconds(1)));
        Assert.Equal(2, calls);
    }
}
