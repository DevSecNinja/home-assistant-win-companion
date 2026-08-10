using WindowsCompanion.Core.Abstractions;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;

namespace WindowsCompanion.Core.Tests;

[Collection(AsyncLifecycleCollection.Name)]
public class WinGetUpdateTests
{
    [Fact]
    public void Structured_output_parses_package_details()
    {
        const string json =
            """{"Packages":[{"Name":"Git","Id":"Git.Git","InstalledVersion":"2.55.0.2","AvailableVersion":"2.55.0.3"}]}""";

        var result = WinGetUpdateResult.Parse(json, DateTimeOffset.UnixEpoch);

        Assert.Equal(WinGetUpdateStatus.Ready, result.Status);
        var package = Assert.Single(result.Packages);
        Assert.Equal("Git.Git", package.Id);
        Assert.Equal("2.55.0.3", package.AvailableVersion);
    }

    [Fact]
    public void Empty_structured_output_means_zero_updates()
    {
        var result = WinGetUpdateResult.Parse(
            """{"Packages":[]}""", DateTimeOffset.UnixEpoch);

        Assert.Equal(WinGetUpdateStatus.Ready, result.Status);
        Assert.Empty(result.Packages);
    }

    [Fact]
    public void Malformed_output_is_not_reported_as_zero()
    {
        var result = WinGetUpdateResult.Parse("not json", DateTimeOffset.UnixEpoch);

        Assert.Equal(WinGetUpdateStatus.InvalidOutput, result.Status);
    }

    [Fact]
    public void Incomplete_package_is_not_reported_as_zero()
    {
        var result = WinGetUpdateResult.Parse(
            """{"Packages":[{"Name":"Git","Id":"Git.Git"}]}""",
            DateTimeOffset.UnixEpoch);

        Assert.Equal(WinGetUpdateStatus.InvalidOutput, result.Status);
    }

    [Theory]
    [InlineData("ModuleMissing", WinGetCapabilityStatus.ModuleMissing)]
    [InlineData("ModuleIncompatible", WinGetCapabilityStatus.ModuleIncompatible)]
    [InlineData("ModuleUntrusted", WinGetCapabilityStatus.ModuleUntrusted)]
    [InlineData("ImportFailed", WinGetCapabilityStatus.ImportFailed)]
    [InlineData("Ready", WinGetCapabilityStatus.Ready)]
    public void Capability_output_preserves_actionable_status(
        string serializedStatus,
        WinGetCapabilityStatus expected)
    {
        var result = WinGetCapabilityResult.Parse(
            $$"""{"Status":"{{serializedStatus}}"}""");

        Assert.Equal(expected, result.Status);
    }

    [Theory]
    [InlineData("ImportFailed", WinGetUpdateStatus.ImportFailed)]
    [InlineData("CommandFailed", WinGetUpdateStatus.CommandFailed)]
    public void Update_output_preserves_execution_failure(
        string serializedStatus,
        WinGetUpdateStatus expected)
    {
        var result = WinGetUpdateResult.Parse(
            $$"""{"Status":"{{serializedStatus}}"}""",
            DateTimeOffset.UnixEpoch);

        Assert.Equal(expected, result.Status);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Disabled_preview_performs_no_provider_query()
    {
        var provider = new FakeProvider();
        var source = new WinGetUpdateSensorSource(provider, new SensorPreferences());

        var preview = await source.PreviewAsync(
            new HashSet<string> { WinGetUpdateSensorSource.WinGetUpdatesId });

        Assert.Equal(0, provider.CheckCount);
        Assert.Equal("Enable to check for updates", Assert.Single(preview).State);
    }

    [Fact]
    public async Task Refresh_reports_only_count_to_home_assistant()
    {
        var provider = new FakeProvider
        {
            Result = new WinGetUpdateResult(
                WinGetUpdateStatus.Ready,
                [new("Git", "Git.Git", "1", "2")])
        };
        var preferences = EnabledPreferences();
        var source = new WinGetUpdateSensorSource(provider, preferences);

        await source.RefreshAsync();
        var reading = Assert.Single(source.Read(
            new HashSet<string> { WinGetUpdateSensorSource.WinGetUpdatesId },
            SensorReadContext.Periodic));

        Assert.Equal(1, reading.State);
        Assert.Null(reading.Attributes);

        var preview = Assert.Single(await source.PreviewAsync(
            new HashSet<string> { WinGetUpdateSensorSource.WinGetUpdatesId }));
        Assert.Contains("Git: 1 -> 2", preview.State?.ToString());
    }

    [Fact]
    public async Task Catalog_refreshes_only_enabled_expensive_sources()
    {
        var provider = new FakeProvider();
        var preferences = new SensorPreferences();
        var source = new WinGetUpdateSensorSource(provider, preferences);
        var catalog = new SensorCatalog([source], preferences);

        await catalog.RefreshAsync();
        Assert.Equal(0, provider.CheckCount);

        preferences.Set(WinGetUpdateSensorSource.WinGetUpdatesId, true);
        await catalog.RefreshAsync();
        Assert.Equal(1, provider.CheckCount);
    }

    [Fact]
    public async Task Stopping_source_cancels_an_active_check()
    {
        var provider = new FakeProvider { BlockUntilCancelled = true };
        var source = new WinGetUpdateSensorSource(provider, EnabledPreferences());

        source.Start(() => { });
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        source.Stop();

        Assert.True(provider.CancellationToken.IsCancellationRequested);
    }

    private static SensorPreferences EnabledPreferences()
    {
        var preferences = new SensorPreferences();
        preferences.Set(WinGetUpdateSensorSource.WinGetUpdatesId, true);
        return preferences;
    }

    private sealed class FakeProvider : IWinGetUpdateProvider
    {
        public int CheckCount { get; private set; }
        public bool BlockUntilCancelled { get; set; }
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken CancellationToken { get; private set; }
        public WinGetUpdateResult Result { get; set; } =
            new(WinGetUpdateStatus.Ready, []);

        public Task<WinGetCapabilityResult> ProbeCapabilityAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(WinGetCapabilityResult.FromStatus(WinGetCapabilityStatus.Ready));

        public async Task<WinGetUpdateResult> CheckForUpdatesAsync(
            CancellationToken cancellationToken = default)
        {
            CheckCount++;
            CancellationToken = cancellationToken;
            Started.TrySetResult();
            if (!BlockUntilCancelled) return Result;

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Result;
        }
    }
}
