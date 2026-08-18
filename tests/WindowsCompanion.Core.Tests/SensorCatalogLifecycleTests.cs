using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;

namespace WindowsCompanion.Core.Tests;

/// <summary>
/// Lifecycle regressions for the catalog that owns every sensor source: a disabled
/// sensor must cost no OS hook and no read, repeated toggling must not stack hooks,
/// and a stopped catalog must not leave a source running or talking back.
/// </summary>
public class SensorCatalogLifecycleTests
{
    [Fact]
    public void A_disabled_source_is_never_started_or_read()
    {
        var source = new CountingSource();
        var catalog = new SensorCatalog([source], new SensorPreferences());

        catalog.Start(() => { });
        var readings = catalog.Read(new SensorReadContext("Test"));

        Assert.Equal(0, source.StartCount);
        Assert.Equal(0, source.ReadCount);
        Assert.Empty(readings);
    }

    [Fact]
    public void Toggling_a_sensor_repeatedly_never_stacks_hooks()
    {
        var source = new CountingSource();
        var catalog = new SensorCatalog([source], new SensorPreferences());
        catalog.Start(() => { });

        for (var cycle = 0; cycle < 25; cycle++)
        {
            catalog.SetEnabled(CountingSource.PrimaryId, true);
            catalog.SetEnabled(CountingSource.PrimaryId, true);
            catalog.SetEnabled(CountingSource.PrimaryId, false);
            catalog.SetEnabled(CountingSource.PrimaryId, false);
        }

        Assert.Equal(25, source.StartCount);
        Assert.Equal(25, source.StopCount);
        Assert.False(source.IsRunning);
    }

    [Fact]
    public void Enabling_the_second_sensor_reuses_the_running_source()
    {
        var source = new CountingSource();
        var catalog = new SensorCatalog([source], new SensorPreferences());
        catalog.Start(() => { });

        catalog.SetEnabled(CountingSource.PrimaryId, true);
        catalog.SetEnabled(CountingSource.SecondaryId, true);

        Assert.Equal(1, source.StartCount);

        // One read, one snapshot, both sensors: no source is asked twice per refresh.
        var readings = catalog.Read(new SensorReadContext("Test"));

        Assert.Equal(1, source.ReadCount);
        Assert.Equal(2, readings.Count);
    }

    [Fact]
    public void Stopping_the_catalog_releases_every_running_source_once()
    {
        var source = new CountingSource();
        var catalog = new SensorCatalog([source], new SensorPreferences());
        catalog.Start(() => { });
        catalog.SetEnabled(CountingSource.PrimaryId, true);

        catalog.Stop();
        catalog.Stop();

        Assert.Equal(1, source.StopCount);
        Assert.False(source.IsRunning);
    }

    [Fact]
    public void A_stopped_catalog_does_not_start_sources_for_later_changes()
    {
        var source = new CountingSource();
        var catalog = new SensorCatalog([source], new SensorPreferences());
        catalog.Start(() => { });
        catalog.Stop();

        catalog.SetEnabled(CountingSource.PrimaryId, true);

        Assert.Equal(0, source.StartCount);
        Assert.False(source.IsRunning);
    }

    [Fact]
    public void A_stopped_source_can_no_longer_ask_for_a_push()
    {
        var source = new CountingSource();
        var catalog = new SensorCatalog([source], new SensorPreferences());
        var pushes = 0;

        catalog.Start(() => pushes++);
        catalog.SetEnabled(CountingSource.PrimaryId, true);
        source.SignalChange();
        Assert.Equal(1, pushes);

        catalog.SetEnabled(CountingSource.PrimaryId, false);
        source.SignalChange();

        Assert.Equal(1, pushes);
    }

    [Fact]
    public async Task A_failing_preview_never_blocks_the_other_sensors()
    {
        var failing = new ThrowingPreviewSource();
        var healthy = new CountingSource();
        var catalog = new SensorCatalog([failing, healthy], new SensorPreferences());

        var preview = await catalog.PreviewAsync();

        Assert.False(preview.ContainsKey(ThrowingPreviewSource.Id));
        Assert.Equal("Primary", preview[CountingSource.PrimaryId]);
        Assert.Equal(0, healthy.StartCount);
    }

    [Fact]
    public void Live_preview_reads_only_enabled_cached_values()
    {
        var preferences = new SensorPreferences();
        var failing = new ThrowingPreviewSource();
        var healthy = new CountingSource();
        var collecting = new UncachedCountingSource();
        var catalog = new SensorCatalog([failing, healthy, collecting], preferences);

        Assert.Empty(catalog.PreviewEnabled());
        Assert.Equal(0, healthy.ReadCount);

        catalog.SetEnabled(ThrowingPreviewSource.Id, true);
        catalog.SetEnabled(CountingSource.PrimaryId, true);
        catalog.SetEnabled(UncachedCountingSource.Id, true);
        var preview = catalog.PreviewEnabled();

        Assert.False(preview.ContainsKey(ThrowingPreviewSource.Id));
        Assert.Equal("Primary", preview[CountingSource.PrimaryId]);
        Assert.Equal(1, healthy.ReadCount);
        Assert.Equal(0, collecting.ReadCount);
    }

    [Fact]
    public async Task Enabling_a_sensitive_sensor_immediately_previews_a_fresh_value()
    {
        var preferences = new SensorPreferences();
        var source = new GatedSensitiveSource();
        var catalog = new SensorCatalog([source], preferences);

        var disabledPreviews = await catalog.PreviewAsync();
        Assert.Equal("Enable to read this value", disabledPreviews[source.Id]);
        Assert.Equal(0, source.CollectionCount);
        Assert.Equal(0, source.StartCount);

        catalog.Start(() => { });

        Assert.Equal(
            "Fresh value",
            await catalog.SetEnabledAndRefreshAsync(source.Id, true));
        Assert.Equal(1, source.CollectionCount);
        Assert.Equal(1, source.StartCount);

        Assert.Equal(
            "Enable to read this value",
            await catalog.SetEnabledAndRefreshAsync(source.Id, false));
        Assert.Equal(1, source.CollectionCount);
        Assert.Equal(1, source.StopCount);
    }

    [Fact]
    public async Task A_single_sensor_preview_surfaces_source_failures()
    {
        var catalog = new SensorCatalog([new ThrowingPreviewSource()], new SensorPreferences());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => catalog.PreviewSensorAsync(ThrowingPreviewSource.Id));
    }

    [Fact]
    public async Task Preview_refreshes_are_single_flight_and_waits_are_cancellable()
    {
        var source = new BlockingPreviewSource();
        var catalog = new SensorCatalog([source], new SensorPreferences());

        var first = catalog.PreviewSensorAsync(BlockingPreviewSource.Id);
        await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using var cancellation = new CancellationTokenSource();
        var second = catalog.PreviewSensorAsync(BlockingPreviewSource.Id, cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.Equal(1, source.MaximumConcurrent);

        source.Release.TrySetResult();
        Assert.Equal("Fresh value", await first);
    }

    [Fact]
    public async Task Enable_refresh_coalesces_source_callbacks_into_the_settings_sync()
    {
        var source = new NotifyingRefreshSource();
        var catalog = new SensorCatalog([source], new SensorPreferences());
        var pushes = 0;
        catalog.Start(() => pushes++);

        await catalog.SetEnabledAndRefreshAsync(NotifyingRefreshSource.Id, true);

        Assert.Equal(1, source.StartCount);
        Assert.Equal(1, source.RefreshCount);
        Assert.Equal(0, pushes);

        source.SignalChange();
        Assert.Equal(1, pushes);
    }

    [Fact]
    public async Task Enable_refresh_returns_the_cached_reading_without_recollecting()
    {
        var source = new CachedRefreshSource();
        var catalog = new SensorCatalog([source], new SensorPreferences());

        var preview = await catalog.SetEnabledAndRefreshAsync(CachedRefreshSource.Id, true);

        Assert.Equal("Fresh value", preview);
        Assert.Equal(1, source.RefreshCount);
        Assert.Equal(1, source.ReadCount);
        Assert.Equal(0, source.PreviewCount);

        var disabledPreview = await catalog.SetEnabledAndRefreshAsync(CachedRefreshSource.Id, false);

        Assert.Equal("Fresh value", disabledPreview);
        Assert.Equal(1, source.RefreshCount);
        Assert.Equal(2, source.ReadCount);
        Assert.Equal(0, source.PreviewCount);
    }

    [Fact]
    public async Task A_start_failure_restores_the_previous_enablement()
    {
        var source = new ThrowingStartSource();
        var catalog = new SensorCatalog([source], new SensorPreferences());
        catalog.Start(() => { });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => catalog.SetEnabledAndRefreshAsync(ThrowingStartSource.Id, true));

        Assert.False(catalog.IsEnabled(ThrowingStartSource.Id));
        Assert.Equal(1, source.StartCount);
    }

    [Fact]
    public async Task Enable_refresh_does_not_suppress_an_unrelated_source()
    {
        var refreshing = new BlockingRefreshSource();
        var unrelated = new CountingSource();
        var catalog = new SensorCatalog([refreshing, unrelated], new SensorPreferences());
        catalog.SetEnabled(CountingSource.PrimaryId, true);
        var pushes = 0;
        catalog.Start(() => pushes++);

        var enabling = catalog.SetEnabledAndRefreshAsync(BlockingRefreshSource.Id, true);
        await refreshing.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        unrelated.SignalChange();
        Assert.Equal(1, pushes);

        refreshing.Release.TrySetResult();
        await enabling;
        Assert.Equal(1, pushes);
    }

    [Fact]
    public async Task A_preview_never_asks_a_source_for_a_sensitive_sensor_that_is_off()
    {
        var source = new RequestRecordingSource();
        var preferences = new SensorPreferences();
        var catalog = new SensorCatalog([source], preferences);

        await catalog.PreviewAsync();
        Assert.Equal([RequestRecordingSource.BenignId], source.LastRequested);

        preferences.Set(RequestRecordingSource.SensitiveId, true);
        await catalog.PreviewAsync();

        Assert.Contains(RequestRecordingSource.SensitiveId, source.LastRequested!);
    }

    private sealed class RequestRecordingSource : ISensorSource
    {
        public const string BenignId = "recording_benign";
        public const string SensitiveId = "recording_sensitive";

        public IReadOnlySet<string>? LastRequested { get; private set; }

        public IReadOnlyList<SensorDefinition> Definitions { get; } =
        [
            new(BenignId, "Benign", "Test sensor.", SensorPrivacy.Benign, false),
            new(SensitiveId, "Sensitive", "Test sensor.", SensorPrivacy.Sensitive, false)
        ];

        public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context)
        {
            LastRequested = enabled;
            return [];
        }

        public void Start(Action onChanged)
        {
        }

        public void Stop()
        {
        }
    }

    private sealed class CountingSource : ISensorSource, ICachedSensorSource
    {
        public const string PrimaryId = "counting_primary";
        public const string SecondaryId = "counting_secondary";

        private Action? _onChanged;

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public int ReadCount { get; private set; }

        public bool IsRunning { get; private set; }

        public IReadOnlyList<SensorDefinition> Definitions { get; } =
        [
            new(PrimaryId, "Primary", "Test sensor.", SensorPrivacy.Benign, false),
            new(SecondaryId, "Secondary", "Test sensor.", SensorPrivacy.Benign, false)
        ];

        public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context)
        {
            ReadCount++;
            return Definitions
                .Where(definition => enabled.Contains(definition.UniqueId))
                .Select(definition => new Sensor
                {
                    UniqueId = definition.UniqueId,
                    Name = definition.Name,
                    State = definition.Name
                })
                .ToList();
        }

        public IReadOnlyList<Sensor> ReadCached(IReadOnlySet<string> enabled) =>
            Read(enabled, new SensorReadContext("Cached"));

        public void Start(Action onChanged)
        {
            StartCount++;
            IsRunning = true;
            _onChanged = onChanged;
        }

        public void Stop()
        {
            StopCount++;
            IsRunning = false;
            _onChanged = null;
        }

        public void SignalChange() => _onChanged?.Invoke();
    }

    private sealed class ThrowingPreviewSource : ISensorSource, ICachedSensorSource
    {
        public const string Id = "throwing_preview";

        public IReadOnlyList<SensorDefinition> Definitions { get; } =
            [new(Id, "Throwing", "Test sensor.", SensorPrivacy.Benign, false)];

        public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context) =>
            throw new InvalidOperationException("Preview must not break the settings UI.");

        public IReadOnlyList<Sensor> ReadCached(IReadOnlySet<string> enabled) =>
            throw new InvalidOperationException("Cached preview must not break the settings UI.");

        public void Start(Action onChanged)
        {
        }

        public void Stop()
        {
        }
    }

    private sealed class UncachedCountingSource : ISensorSource
    {
        public const string Id = "uncached_counting";

        public int ReadCount { get; private set; }

        public IReadOnlyList<SensorDefinition> Definitions { get; } =
            [new(Id, "Uncached", "Test sensor.", SensorPrivacy.Benign, false)];

        public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context)
        {
            ReadCount++;
            return [new Sensor { UniqueId = Id, State = "Collected" }];
        }

        public void Start(Action onChanged) { }

        public void Stop() { }
    }

    private sealed class GatedSensitiveSource : ISensorSource
    {
        public string Id => "sensitive_value";

        public int CollectionCount { get; private set; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public IReadOnlyList<SensorDefinition> Definitions =>
            [new(Id, "Sensitive", "Test sensor.", SensorPrivacy.Sensitive, false)];

        public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context) => [];

        public ValueTask<IReadOnlyList<Sensor>> PreviewAsync(
            IReadOnlySet<string> requested,
            CancellationToken cancellationToken = default)
        {
            CollectionCount++;
            return ValueTask.FromResult<IReadOnlyList<Sensor>>(
                [new Sensor { UniqueId = Id, State = "Fresh value" }]);
        }

        public void Start(Action onChanged) => StartCount++;

        public void Stop() => StopCount++;
    }

    private sealed class BlockingPreviewSource : ISensorSource
    {
        private int _concurrent;

        public const string Id = "blocking_preview";

        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaximumConcurrent { get; private set; }

        public IReadOnlyList<SensorDefinition> Definitions { get; } =
            [new(Id, "Blocking", "Test sensor.", SensorPrivacy.Benign, false)];

        public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context) => [];

        public async ValueTask<IReadOnlyList<Sensor>> PreviewAsync(
            IReadOnlySet<string> requested,
            CancellationToken cancellationToken = default)
        {
            var concurrent = Interlocked.Increment(ref _concurrent);
            MaximumConcurrent = Math.Max(MaximumConcurrent, concurrent);
            Entered.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
                return [new Sensor { UniqueId = Id, State = "Fresh value" }];
            }
            finally
            {
                Interlocked.Decrement(ref _concurrent);
            }
        }

        public void Start(Action onChanged)
        {
        }

        public void Stop()
        {
        }
    }

    private sealed class NotifyingRefreshSource : ISensorSource, IRefreshableSensorSource
    {
        private Action? _onChanged;

        public const string Id = "notifying_refresh";

        public int StartCount { get; private set; }

        public int RefreshCount { get; private set; }

        public IReadOnlyList<SensorDefinition> Definitions { get; } =
            [new(Id, "Notifying", "Test sensor.", SensorPrivacy.Benign, false)];

        public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context) =>
            enabled.Contains(Id)
                ? [new Sensor { UniqueId = Id, State = "Fresh value" }]
                : [];

        public void Start(Action onChanged)
        {
            StartCount++;
            _onChanged = onChanged;
            _onChanged();
        }

        public void Stop() => _onChanged = null;

        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            _onChanged?.Invoke();
            return Task.CompletedTask;
        }

        public void SignalChange() => _onChanged?.Invoke();
    }

    private sealed class BlockingRefreshSource : ISensorSource, IRefreshableSensorSource
    {
        private Action? _onChanged;

        public const string Id = "blocking_refresh";

        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<SensorDefinition> Definitions { get; } =
            [new(Id, "Blocking refresh", "Test sensor.", SensorPrivacy.Benign, false)];

        public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context) =>
            [];

        public void Start(Action onChanged)
        {
            _onChanged = onChanged;
            _onChanged();
        }

        public void Stop() => _onChanged = null;

        public async Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            _onChanged?.Invoke();
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class CachedRefreshSource : ISensorSource, IRefreshableSensorSource
    {
        public const string Id = "cached_refresh";

        private string _value = "Stale value";

        public int ReadCount { get; private set; }

        public int PreviewCount { get; private set; }

        public int RefreshCount { get; private set; }

        public IReadOnlyList<SensorDefinition> Definitions { get; } =
            [new(Id, "Cached refresh", "Test sensor.", SensorPrivacy.Benign, false)];

        public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context)
        {
            ReadCount++;
            return enabled.Contains(Id)
                ? [new Sensor { UniqueId = Id, State = _value }]
                : [];
        }

        public ValueTask<IReadOnlyList<Sensor>> PreviewAsync(
            IReadOnlySet<string> requested,
            CancellationToken cancellationToken = default)
        {
            PreviewCount++;
            return ValueTask.FromResult<IReadOnlyList<Sensor>>(
                [new Sensor { UniqueId = Id, State = "Collected again" }]);
        }

        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            _value = "Fresh value";
            return Task.CompletedTask;
        }

        public void Start(Action onChanged)
        {
        }

        public void Stop()
        {
        }
    }

    private sealed class ThrowingStartSource : ISensorSource
    {
        public const string Id = "throwing_start";

        public int StartCount { get; private set; }

        public IReadOnlyList<SensorDefinition> Definitions { get; } =
            [new(Id, "Throwing start", "Test sensor.", SensorPrivacy.Benign, false)];

        public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context) =>
            [];

        public void Start(Action onChanged)
        {
            StartCount++;
            throw new InvalidOperationException("Start failed.");
        }

        public void Stop()
        {
        }
    }
}
