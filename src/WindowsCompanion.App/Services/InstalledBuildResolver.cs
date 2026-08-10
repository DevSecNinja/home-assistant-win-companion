using System.Reflection;
using WindowsCompanion.Core.Updates;

namespace WindowsCompanion_App.Services;

internal sealed record InstalledBuildInfo(bool IsOfficialRelease, SemanticVersion? Version);

/// <summary>Distinguishes shipped release binaries from source and CI builds.</summary>
internal static class InstalledBuildResolver
{
    internal static InstalledBuildInfo Current()
    {
        var informationalVersion = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

#if OFFICIAL_BUILD
        const bool officialRelease = true;
#else
        const bool officialRelease = false;
#endif

        return Resolve(informationalVersion, officialRelease);
    }

    internal static InstalledBuildInfo Resolve(
        string? informationalVersion,
        bool officialRelease) =>
        new(
            officialRelease,
            officialRelease
            && SemanticVersion.TryParse(informationalVersion, out var version)
                ? version
                : null);
}
