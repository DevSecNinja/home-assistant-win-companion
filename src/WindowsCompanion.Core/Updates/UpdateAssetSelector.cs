namespace WindowsCompanion.Core.Updates;

/// <summary>Supported CPU architectures for the published setup packages.</summary>
public enum UpdateArchitecture
{
    X64,
    Arm64
}

/// <summary>
/// The setup package matching this process's architecture, plus its checksum
/// sidecar asset when both are published on the release.
/// </summary>
public sealed record SelectedUpdateAsset(
    ReleaseAsset Package,
    ReleaseAsset Checksum);

/// <summary>
/// Picks the architecture-matched setup package (and its checksum sidecar) from
/// a release's assets. Pure logic: the caller supplies the running process's
/// architecture and the release version so this stays testable without touching
/// <c>RuntimeInformation</c> directly.
/// </summary>
public static class UpdateAssetSelector
{
    public static SelectedUpdateAsset? Select(
        SemanticVersion version,
        IReadOnlyList<ReleaseAsset> assets,
        UpdateArchitecture architecture)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(assets);

        var archName = architecture == UpdateArchitecture.Arm64 ? "arm64" : "x64";
        var packageName = $"WindowsCompanion-{version}-win-{archName}-setup.zip";
        var checksumName = packageName + ".sha256";

        ReleaseAsset? package = null;
        ReleaseAsset? checksum = null;
        foreach (var asset in assets)
        {
            if (string.Equals(asset.Name, packageName, StringComparison.Ordinal))
                package = asset;
            else if (string.Equals(asset.Name, checksumName, StringComparison.Ordinal))
                checksum = asset;
        }

        return package is null || checksum is null
            ? null
            : new SelectedUpdateAsset(package, checksum);
    }
}
