using WindowsCompanion.Core.Updates;

namespace WindowsCompanion.Core.Tests;

public class UpdateInstallerTests
{
    private static SemanticVersion Version(string value)
    {
        Assert.True(SemanticVersion.TryParse(value, out var version));
        return version!;
    }

    private static AvailableUpdate Update(string version, bool withAssets = true) => new(
        Version("1.0.0"),
        Version(version),
        new Uri($"https://github.com/DevSecNinja/home-assistant-win-companion/releases/tag/v{version}"),
        withAssets
            ?
            [
                new ReleaseAsset(
                    $"WindowsCompanion-{version}-win-x64-setup.zip",
                    "https://example.invalid/setup.zip"),
                new ReleaseAsset(
                    $"WindowsCompanion-{version}-win-x64-setup.zip.sha256",
                    "https://example.invalid/setup.zip.sha256"),
            ]
            : Array.Empty<ReleaseAsset>());

    private sealed class ScriptedDownloader(
        Func<SelectedUpdateAsset, IProgress<double>, CancellationToken, Task<string>> download)
        : IUpdatePackageDownloader
    {
        public Task<string> DownloadAsync(
            SelectedUpdateAsset asset,
            SemanticVersion version,
            IProgress<double> progress,
            CancellationToken cancellationToken) =>
            download(asset, progress, cancellationToken);
    }

    private sealed class ScriptedVerifier(
        Func<string, CancellationToken, Task> verify) : IUpdatePackageVerifier
    {
        public Task VerifyAsync(
            string packagePath,
            SelectedUpdateAsset asset,
            CancellationToken cancellationToken) =>
            verify(packagePath, cancellationToken);
    }

    private sealed class ScriptedInstaller(
        Func<string, CancellationToken, Task> install) : IUpdatePackageInstaller
    {
        public Task InstallAsync(
            string packagePath,
            SemanticVersion version,
            CancellationToken cancellationToken) =>
            install(packagePath, cancellationToken);
    }

    private static UpdateInstaller Installer(
        IUpdatePackageDownloader? downloader = null,
        IUpdatePackageVerifier? verifier = null,
        IUpdatePackageInstaller? installer = null) => new(
        downloader ?? new ScriptedDownloader(
            (_, _, _) => Task.FromResult("C:\\fake\\package.zip")),
        verifier ?? new ScriptedVerifier((_, _) => Task.CompletedTask),
        installer ?? new ScriptedInstaller((_, _) => Task.CompletedTask));

    [Fact]
    public async Task Successful_download_and_verification_reaches_ready_to_install()
    {
        var installer = Installer();
        var states = new List<UpdateInstallState>();
        installer.StateChanged += states.Add;

        await installer.DownloadAsync(Update("1.4.0"), UpdateArchitecture.X64);

        Assert.Equal(UpdateInstallPhase.ReadyToInstall, installer.State.Phase);
        Assert.Contains(states, s => s.Phase == UpdateInstallPhase.Downloading);
        Assert.Contains(states, s => s.Phase == UpdateInstallPhase.Verifying);
    }

    [Fact]
    public async Task Missing_architecture_asset_fails_without_downloading()
    {
        var downloadStarted = false;
        var installer = Installer(downloader: new ScriptedDownloader((_, _, _) =>
        {
            downloadStarted = true;
            return Task.FromResult("C:\\fake\\package.zip");
        }));

        await installer.DownloadAsync(Update("1.4.0", withAssets: false), UpdateArchitecture.X64);

        Assert.False(downloadStarted);
        Assert.Equal(UpdateInstallPhase.Failed, installer.State.Phase);
    }

    [Fact]
    public async Task Download_failure_produces_a_failed_state()
    {
        var installer = Installer(downloader: new ScriptedDownloader(
            (_, _, _) => throw new HttpRequestException("boom")));

        await installer.DownloadAsync(Update("1.4.0"), UpdateArchitecture.X64);

        Assert.Equal(UpdateInstallPhase.Failed, installer.State.Phase);
    }

    [Fact]
    public async Task Verification_failure_produces_a_failed_state_and_does_not_offer_install()
    {
        var installer = Installer(verifier: new ScriptedVerifier(
            (_, _) => throw new UpdatePackageVerificationException("checksum mismatch")));

        await installer.DownloadAsync(Update("1.4.0"), UpdateArchitecture.X64);

        Assert.Equal(UpdateInstallPhase.Failed, installer.State.Phase);
        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync());
    }

    [Fact]
    public async Task Install_requires_a_ready_state()
    {
        var installer = Installer();

        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync());
    }

    [Fact]
    public async Task Install_runs_the_installer_once_ready()
    {
        var installed = false;
        var installer = Installer(installer: new ScriptedInstaller((_, _) =>
        {
            installed = true;
            return Task.CompletedTask;
        }));

        await installer.DownloadAsync(Update("1.4.0"), UpdateArchitecture.X64);
        await installer.InstallAsync();

        Assert.True(installed);
        Assert.Equal(UpdateInstallPhase.Installed, installer.State.Phase);
    }

    [Fact]
    public async Task A_newer_download_supersedes_an_older_in_flight_one()
    {
        var firstGate = new TaskCompletionSource();
        var installer = Installer(downloader: new ScriptedDownloader((asset, _, ct) =>
            asset.Package.Name.Contains("1.4.0", StringComparison.Ordinal)
                ? BlockUntilGateAsync(firstGate, ct)
                : Task.FromResult("C:\\fake\\second.zip")));

        var first = installer.DownloadAsync(Update("1.4.0"), UpdateArchitecture.X64);
        await installer.DownloadAsync(Update("1.5.0"), UpdateArchitecture.X64);

        Assert.Equal(UpdateInstallPhase.ReadyToInstall, installer.State.Phase);
        Assert.Equal("1.5.0", installer.State.Version.ToString());

        firstGate.TrySetResult();
        await first;

        // The superseded run's completion must not overwrite the newer result.
        Assert.Equal("1.5.0", installer.State.Version.ToString());
    }

    private static async Task<string> BlockUntilGateAsync(
        TaskCompletionSource gate, CancellationToken ct)
    {
        await gate.Task.WaitAsync(ct);
        return "C:\\fake\\first.zip";
    }
}
