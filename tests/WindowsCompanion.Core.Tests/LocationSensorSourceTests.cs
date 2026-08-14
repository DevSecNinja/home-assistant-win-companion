using WindowsCompanion.Core.Abstractions;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;

namespace WindowsCompanion.Core.Tests;

[Collection(AsyncLifecycleCollection.Name)]
public class LocationSensorSourceTests
{
    [Fact]
    public async Task Scheduled_poll_reports_initial_position_and_suppresses_jitter_until_meaningful_movement()
    {
        var provider = new GatedFakeProvider(
        [
            LocationResult.Ready(47.000000, 8.000000, 5),
            LocationResult.Ready(47.000050, 8.000050, 5), // jitter: ~5.5 m, below the ~11 m threshold
            LocationResult.Ready(47.010000, 8.010000, 5) // meaningful movement
        ]);
        var changed = new SemaphoreSlim(0);
        var source = new LocationSensorSource(
            provider,
            EnabledPreferences(),
            refreshInterval: TimeSpan.FromMilliseconds(1));

        source.Start(() => changed.Release());
        try
        {
            // Start() always ticks immediately, and the default published state
            // differs from any real fix, so the very first fix is always news.
            provider.AllowNextCall();
            Assert.True(
                await changed.WaitAsync(TimeSpan.FromSeconds(2)),
                "onChanged was not invoked for the initial scheduled fix.");
            var first = Assert.Single(source.Read(
                new HashSet<string> { LocationSensorSource.LocationId }, SensorReadContext.Periodic));
            Assert.Equal("47.000000,8.000000", first.State);

            // The second scheduled tick lands within the jitter threshold: the
            // cached reading still updates, but onChanged must not fire again.
            // The next call is gated behind AllowNextCall(), so no further tick
            // can race ahead while this is checked. Poll Read() itself (with a
            // timeout) rather than a fixed delay, so the assertion waits for
            // exactly as long as the in-flight tick needs to finish updating
            // the cache instead of guessing at a "long enough" sleep.
            provider.AllowNextCall();
            var jittered = await WaitForStateAsync(
                source, "47.000050,8.000050", TimeSpan.FromSeconds(2));
            Assert.Equal(0, changed.CurrentCount);
            Assert.Equal("47.000050,8.000050", jittered.State);

            // The third tick moves far enough to be real news again.
            provider.AllowNextCall();
            Assert.True(
                await changed.WaitAsync(TimeSpan.FromSeconds(2)),
                "onChanged was not invoked for the meaningfully moved fix.");
            var moved = Assert.Single(source.Read(
                new HashSet<string> { LocationSensorSource.LocationId }, SensorReadContext.Periodic));
            Assert.Equal("47.010000,8.010000", moved.State);
        }
        finally
        {
            source.Stop();
        }
    }

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
        Assert.Equal(47.398000, reading.Attributes!["latitude"]);
        Assert.Equal(8.545100, reading.Attributes!["longitude"]);
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

    /// <summary>
    /// Polls <see cref="LocationSensorSource.Read"/> until it reports the
    /// expected state or the timeout elapses, so the assertion waits for
    /// exactly as long as an in-flight tick needs to settle instead of
    /// guessing at a fixed delay (which raced the cache update under load).
    /// </summary>
    private static async Task<Sensor> WaitForStateAsync(
        LocationSensorSource source, string expectedState, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var reading = Assert.Single(source.Read(
                new HashSet<string> { LocationSensorSource.LocationId }, SensorReadContext.Periodic));
            if (Equals(reading.State, expectedState)) return reading;

            if (DateTime.UtcNow > deadline)
                throw new TimeoutException(
                    $"Location sensor never reported state \"{expectedState}\" "
                    + $"(last seen: \"{reading.State}\").");
            await Task.Delay(5);
        }
    }

    /// <summary>
    /// Blocks each call until the test explicitly allows it via
    /// <see cref="AllowNextCall"/>, so a fast poll interval cannot race ahead
    /// of the assertions checking whether a given tick pushed a change.
    /// </summary>
    private sealed class GatedFakeProvider(LocationResult[] results) : ILocationProvider
    {
        private readonly SemaphoreSlim _gate = new(0);
        private int _index;

        public int CallCount { get; private set; }

        public void AllowNextCall() => _gate.Release();

        public async Task<LocationResult> GetLocationAsync(
            CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            var index = Math.Min(_index, results.Length - 1);
            CallCount++;
            _index++;
            return results[index];
        }
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
