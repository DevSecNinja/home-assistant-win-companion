using WindowsCompanion.Core.Abstractions;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;

namespace WindowsCompanion.Core.Tests;

[Collection(AsyncLifecycleCollection.Name)]
public class LocationSensorSourceTests
{
    [Fact]
    public async Task Disabled_preview_performs_no_provider_query()
    {
        var provider = new FakeProvider();
        var source = new LocationSensorSource(provider, new SensorPreferences());

        var preview = await source.PreviewAsync(
            new HashSet<string> { LocationSensorSource.LocationId });

        Assert.Equal(0, provider.CallCount);
        Assert.Equal(
            "Enable to read this device's location",
            Assert.Single(preview).State);
    }

    [Fact]
    public void Location_definition_is_sensitive_and_off_by_default()
    {
        var source = new LocationSensorSource(new FakeProvider(), new SensorPreferences());

        var definition = Assert.Single(source.Definitions);
        Assert.Equal(SensorPrivacy.Sensitive, definition.Privacy);
        Assert.False(definition.EnabledByDefault);
    }

    [Fact]
    public async Task Enable_reports_ready_coordinate_with_accuracy_attribute()
    {
        var provider = new FakeProvider
        {
            Result = LocationResult.Ready(47.398000, 8.545100, 12.5)
        };
        var source = new LocationSensorSource(provider, EnabledPreferences());

        await source.RefreshAsync();
        var reading = Assert.Single(source.Read(
            new HashSet<string> { LocationSensorSource.LocationId },
            SensorReadContext.Periodic));

        Assert.Equal("47.398000,8.545100", reading.State);
        Assert.NotNull(reading.Attributes);
        Assert.Equal(12.5, reading.Attributes!["gps_accuracy"]);
    }

    [Fact]
    public async Task Refresh_reports_updated_coordinate_on_next_poll()
    {
        var provider = new FakeProvider
        {
            Result = LocationResult.Ready(1.000000, 2.000000, 5)
        };
        var source = new LocationSensorSource(provider, EnabledPreferences());

        await source.RefreshAsync();
        var first = Assert.Single(source.Read(
            new HashSet<string> { LocationSensorSource.LocationId },
            SensorReadContext.Periodic));
        Assert.Equal("1.000000,2.000000", first.State);

        provider.Result = LocationResult.Ready(3.000000, 4.000000, 5);
        await source.RefreshAsync();
        var second = Assert.Single(source.Read(
            new HashSet<string> { LocationSensorSource.LocationId },
            SensorReadContext.Periodic));
        Assert.Equal("3.000000,4.000000", second.State);
    }

    [Fact]
    public async Task PermissionDenied_result_reports_actionable_state()
    {
        var provider = new FakeProvider
        {
            Result = LocationResult.Unavailable(LocationStatus.PermissionDenied)
        };
        var source = new LocationSensorSource(provider, EnabledPreferences());

        await source.RefreshAsync();
        var reading = Assert.Single(source.Read(
            new HashSet<string> { LocationSensorSource.LocationId },
            SensorReadContext.Periodic));

        Assert.Equal("Location permission required", reading.State);
        Assert.Null(reading.Attributes);
    }

    [Fact]
    public async Task Unavailable_result_reports_unavailable_state()
    {
        var provider = new FakeProvider
        {
            Result = LocationResult.Unavailable()
        };
        var source = new LocationSensorSource(provider, EnabledPreferences());

        await source.RefreshAsync();
        var reading = Assert.Single(source.Read(
            new HashSet<string> { LocationSensorSource.LocationId },
            SensorReadContext.Periodic));

        Assert.Equal("Unavailable", reading.State);
        Assert.Null(reading.Attributes);
    }

    [Fact]
    public async Task Catalog_refreshes_only_enabled_expensive_sources()
    {
        var provider = new FakeProvider();
        var preferences = new SensorPreferences();
        var source = new LocationSensorSource(provider, preferences);
        var catalog = new SensorCatalog([source], preferences);

        await catalog.RefreshAsync();
        Assert.Equal(0, provider.CallCount);

        preferences.Set(LocationSensorSource.LocationId, true);
        await catalog.RefreshAsync();
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task Stopping_source_cancels_an_active_query()
    {
        var provider = new FakeProvider { BlockUntilCancelled = true };
        var source = new LocationSensorSource(provider, EnabledPreferences());

        source.Start(() => { });
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        source.Stop();

        Assert.True(provider.CancellationToken.IsCancellationRequested);
    }

    private static SensorPreferences EnabledPreferences()
    {
        var preferences = new SensorPreferences();
        preferences.Set(LocationSensorSource.LocationId, true);
        return preferences;
    }

    private sealed class FakeProvider : ILocationProvider
    {
        public int CallCount { get; private set; }
        public bool BlockUntilCancelled { get; set; }
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken CancellationToken { get; private set; }
        public LocationResult Result { get; set; } = LocationResult.Unavailable();

        public async Task<LocationResult> GetLocationAsync(
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            CancellationToken = cancellationToken;
            Started.TrySetResult();
            if (!BlockUntilCancelled) return Result;

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Result;
        }
    }
}
