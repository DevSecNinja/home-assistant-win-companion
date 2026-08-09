using HaCompanion.Core.Abstractions;
using HaCompanion.Core.Models;
using HaCompanion.Core.Sensors;

namespace HaCompanion.Core.Tests;

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

        Assert.True(provider.Cancelled.Task.IsCompletedSuccessfully);
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
        public TaskCompletionSource Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public WinGetUpdateResult Result { get; set; } =
            new(WinGetUpdateStatus.Ready, []);

        public Task<bool> IsModuleInstalledAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public async Task<WinGetUpdateResult> CheckForUpdatesAsync(
            CancellationToken cancellationToken = default)
        {
            CheckCount++;
            if (!BlockUntilCancelled) return Result;

            Started.TrySetResult();
            using var registration = cancellationToken.Register(
                () => Cancelled.TrySetResult());
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Result;
        }
    }
}
