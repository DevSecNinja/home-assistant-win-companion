using HaCompanion.Core.Models;
using HaCompanion.Core.Sensors;

namespace HaCompanion.Core.Tests;

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

    private sealed class CountingSource : ISensorSource
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

    private sealed class ThrowingPreviewSource : ISensorSource
    {
        public const string Id = "throwing_preview";

        public IReadOnlyList<SensorDefinition> Definitions { get; } =
            [new(Id, "Throwing", "Test sensor.", SensorPrivacy.Benign, false)];

        public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context) =>
            throw new InvalidOperationException("Preview must not break the settings UI.");

        public void Start(Action onChanged)
        {
        }

        public void Stop()
        {
        }
    }
}
