namespace WindowsCompanion.Core.App;

/// <summary>
/// Resolves the per-user data directory and migrates the one used before the
/// product was renamed to "Windows Companion for Home Assistant".
/// </summary>
/// <remarks>
/// Settings, the lifecycle journal and logs all live under a single directory,
/// so migrating it is a single directory move rather than a per-file copy. The
/// move must happen before any of those files is opened, otherwise a freshly
/// created empty file would block the migration and the previous Home Assistant
/// registration would be orphaned - which shows up as a duplicate device.
/// </remarks>
public static class AppDataPaths
{
    /// <summary>Directory name used from the rename onwards.</summary>
    public const string DirectoryName = "WindowsCompanion";

    /// <summary>Directory name used before the rename.</summary>
    public const string LegacyDirectoryName = "HaCompanion";

    /// <summary>
    /// Returns the data directory to use, migrating the legacy directory first
    /// when that is possible.
    /// </summary>
    /// <param name="localApplicationData">
    /// Root to resolve against. Defaults to <c>%LOCALAPPDATA%</c>; tests pass a
    /// temporary directory.
    /// </param>
    public static string Resolve(string? localApplicationData = null)
    {
        var root = localApplicationData
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var current = Path.Combine(root, DirectoryName);
        var legacy = Path.Combine(root, LegacyDirectoryName);

        return Migrate(legacy, current);
    }

    /// <summary>
    /// Moves <paramref name="legacy"/> to <paramref name="current"/> when the
    /// migration has not already happened, and returns the directory to use.
    /// </summary>
    /// <returns>
    /// <paramref name="current"/> normally. If the move fails - the directory is
    /// locked, or the user lacks permission - the legacy directory is returned so
    /// an existing registration keeps working rather than being silently
    /// abandoned.
    /// </returns>
    internal static string Migrate(string legacy, string current)
    {
        // Already migrated, or a fresh install. Never merge two directories:
        // whichever one "current" is wins, so repeated calls are idempotent.
        if (Directory.Exists(current)) return current;
        if (!Directory.Exists(legacy)) return current;

        try
        {
            Directory.Move(legacy, current);
            return current;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return legacy;
        }
    }
}
