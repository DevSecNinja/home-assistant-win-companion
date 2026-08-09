using WindowsCompanion.Core.App;

namespace WindowsCompanion.Core.Tests;

/// <summary>
/// The data directory migration runs before settings, credentials or the
/// lifecycle journal are read. Getting it wrong orphans an existing Home
/// Assistant registration, which surfaces as a duplicate device.
/// </summary>
public sealed class AppDataPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"wc-paths-{Guid.NewGuid():N}");

    public AppDataPathsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Legacy => Path.Combine(_root, AppDataPaths.LegacyDirectoryName);

    private string Current => Path.Combine(_root, AppDataPaths.DirectoryName);

    [Fact]
    public void A_fresh_install_uses_the_current_directory()
    {
        Assert.Equal(Current, AppDataPaths.Resolve(_root));
    }

    [Fact]
    public void An_existing_legacy_directory_is_moved_with_its_contents()
    {
        Directory.CreateDirectory(Legacy);
        File.WriteAllText(Path.Combine(Legacy, "settings.json"), "{}");
        Directory.CreateDirectory(Path.Combine(Legacy, "logs"));

        var resolved = AppDataPaths.Resolve(_root);

        Assert.Equal(Current, resolved);
        Assert.False(Directory.Exists(Legacy));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(Current, "settings.json")));
        Assert.True(Directory.Exists(Path.Combine(Current, "logs")));
    }

    [Fact]
    public void Migration_is_idempotent()
    {
        Directory.CreateDirectory(Legacy);
        File.WriteAllText(Path.Combine(Legacy, "settings.json"), "first");

        AppDataPaths.Resolve(_root);
        var second = AppDataPaths.Resolve(_root);

        Assert.Equal(Current, second);
        Assert.Equal("first", File.ReadAllText(Path.Combine(Current, "settings.json")));
    }

    [Fact]
    public void An_already_migrated_directory_is_never_overwritten_by_the_legacy_one()
    {
        Directory.CreateDirectory(Legacy);
        File.WriteAllText(Path.Combine(Legacy, "settings.json"), "stale");
        Directory.CreateDirectory(Current);
        File.WriteAllText(Path.Combine(Current, "settings.json"), "live");

        var resolved = AppDataPaths.Resolve(_root);

        Assert.Equal(Current, resolved);
        Assert.Equal("live", File.ReadAllText(Path.Combine(Current, "settings.json")));
    }

    [Fact]
    public void A_failed_move_falls_back_to_the_legacy_directory()
    {
        Directory.CreateDirectory(Legacy);
        var locked = Path.Combine(Legacy, "settings.json");
        File.WriteAllText(locked, "{}");

        // Holding an open handle makes Directory.Move fail on Windows, which is
        // the realistic failure: the previous instance is still shutting down.
        using var handle = new FileStream(
            locked, FileMode.Open, FileAccess.Read, FileShare.None);

        var resolved = AppDataPaths.Resolve(_root);

        Assert.Equal(Legacy, resolved);
        Assert.True(File.Exists(locked));
    }
}
