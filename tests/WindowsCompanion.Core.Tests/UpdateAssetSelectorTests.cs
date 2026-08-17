using WindowsCompanion.Core.Updates;

namespace WindowsCompanion.Core.Tests;

public class UpdateAssetSelectorTests
{
    private static SemanticVersion Version(string value)
    {
        Assert.True(SemanticVersion.TryParse(value, out var version));
        return version!;
    }

    [Fact]
    public void Selects_the_matching_architecture_package_and_its_checksum()
    {
        var assets = new[]
        {
            new ReleaseAsset(
                "WindowsCompanion-1.4.0-win-x64-setup.zip",
                "https://example.invalid/x64.zip"),
            new ReleaseAsset(
                "WindowsCompanion-1.4.0-win-x64-setup.zip.sha256",
                "https://example.invalid/x64.zip.sha256"),
            new ReleaseAsset(
                "WindowsCompanion-1.4.0-win-arm64-setup.zip",
                "https://example.invalid/arm64.zip"),
            new ReleaseAsset(
                "WindowsCompanion-1.4.0-win-arm64-setup.zip.sha256",
                "https://example.invalid/arm64.zip.sha256"),
        };

        var selected = UpdateAssetSelector.Select(Version("1.4.0"), assets, UpdateArchitecture.Arm64);

        Assert.NotNull(selected);
        Assert.Equal("WindowsCompanion-1.4.0-win-arm64-setup.zip", selected.Package.Name);
        Assert.Equal("WindowsCompanion-1.4.0-win-arm64-setup.zip.sha256", selected.Checksum.Name);
    }

    [Fact]
    public void Returns_null_when_the_package_asset_is_missing()
    {
        var assets = new[]
        {
            new ReleaseAsset(
                "WindowsCompanion-1.4.0-win-x64-setup.zip.sha256",
                "https://example.invalid/x64.zip.sha256"),
        };

        Assert.Null(UpdateAssetSelector.Select(Version("1.4.0"), assets, UpdateArchitecture.X64));
    }

    [Fact]
    public void Returns_null_when_the_checksum_sidecar_is_missing()
    {
        var assets = new[]
        {
            new ReleaseAsset(
                "WindowsCompanion-1.4.0-win-x64-setup.zip",
                "https://example.invalid/x64.zip"),
        };

        Assert.Null(UpdateAssetSelector.Select(Version("1.4.0"), assets, UpdateArchitecture.X64));
    }

    [Fact]
    public void Does_not_match_a_different_version_or_architecture()
    {
        var assets = new[]
        {
            new ReleaseAsset(
                "WindowsCompanion-1.3.0-win-x64-setup.zip",
                "https://example.invalid/x64.zip"),
            new ReleaseAsset(
                "WindowsCompanion-1.3.0-win-x64-setup.zip.sha256",
                "https://example.invalid/x64.zip.sha256"),
        };

        Assert.Null(UpdateAssetSelector.Select(Version("1.4.0"), assets, UpdateArchitecture.X64));
        Assert.Null(UpdateAssetSelector.Select(Version("1.3.0"), assets, UpdateArchitecture.Arm64));
    }
}
