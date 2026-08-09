namespace WindowsCompanion.Core.Sensors;

/// <summary>
/// Builds the human-readable Windows version string from the raw registry values.
/// </summary>
public static class OsVersionFormatter
{
    /// <summary>Builds above this go by "Windows 11" despite what the registry says.</summary>
    public const int FirstWindows11Build = 22000;

    /// <summary>
    /// Combines product name, display version and build into something a user
    /// recognises, e.g. "Windows 11 Pro 24H2 26100.2314".
    /// </summary>
    /// <remarks>
    /// Windows 11 still reports "Windows 10 ..." in <c>ProductName</c>, so the
    /// product is corrected using the build number. Any missing part is simply
    /// omitted rather than rendered as an empty gap.
    /// </remarks>
    public static string Describe(
        string? productName,
        string? displayVersion,
        string? currentBuild,
        string? updateBuildRevision,
        string fallback)
    {
        var product = productName;

        if (product is not null
            && int.TryParse(currentBuild, out var build)
            && build >= FirstWindows11Build)
        {
            product = product.Replace("Windows 10", "Windows 11");
        }

        var version = string.IsNullOrEmpty(updateBuildRevision)
            ? currentBuild
            : $"{currentBuild}.{updateBuildRevision}";

        var text = string.Join(' ', new[] { product, displayVersion, version }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }
}
