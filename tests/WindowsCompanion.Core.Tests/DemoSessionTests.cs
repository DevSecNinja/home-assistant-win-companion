using WindowsCompanion.Core.App;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;

namespace WindowsCompanion.Core.Tests;

/// <summary>
/// Demo mode exists so someone without a Home Assistant server can see what this
/// PC would report. That promise only holds if the demo reads locally on request
/// and never starts a source: a started source installs OS hooks, polls and asks
/// for pushes, which is collection the user did not ask for.
/// </summary>
public class DemoSessionTests
{
    [Fact]
    public async Task A_demo_shows_every_sensor_and_its_current_value()
    {
        var source = new CountingSource();
        var demo = new DemoSession([source]);

        var preview = await demo.PreviewAsync();

        Assert.Equal(
            [CountingSource.PrimaryId, CountingSource.SecondaryId],
            demo.Catalog.Definitions.Select(definition => definition.UniqueId));
        Assert.Equal("Primary", preview[CountingSource.PrimaryId]);
        Assert.Equal(0, source.StartCount);
    }

    [Fact]
    public async Task Switching_a_sensor_on_in_the_demo_never_starts_collecting()
    {
        var source = new CountingSource();
        var demo = new DemoSession([source]);

        demo.SetSensorEnabled(CountingSource.PrimaryId, true);
        await demo.PreviewAsync();

        Assert.True(demo.Catalog.IsEnabled(CountingSource.PrimaryId));
        Assert.Equal(0, source.StartCount);
        Assert.False(source.IsRunning);
    }

    [Fact]
    public void Demo_choices_stay_in_the_session_and_are_never_persisted()
    {
        var preferences = new SensorPreferences();
        var demo = new DemoSession([new CountingSource()], preferences);

        demo.SetSensorEnabled(CountingSource.PrimaryId, true);

        // The caller owns the preferences instance; nothing here reaches a store.
        Assert.Same(preferences, demo.Preferences);
        Assert.True(preferences.Enabled[CountingSource.PrimaryId]);
    }

    [Fact]
    public void Ending_the_demo_leaves_no_source_running()
    {
        var source = new CountingSource();
        var demo = new DemoSession([source]);
        demo.SetSensorEnabled(CountingSource.PrimaryId, true);

        demo.End();

        Assert.False(source.IsRunning);
        Assert.Equal(0, source.StartCount);
    }

    [Fact]
    public async Task A_sensitive_sensor_is_not_read_by_the_demo_until_it_is_switched_on()
    {
        var source = new SensitiveSource();
        var demo = new DemoSession([source]);

        var before = await demo.PreviewAsync();
        Assert.False(before.ContainsKey(SensitiveSource.Id));
        Assert.Equal(0, source.ReadCount);

        demo.SetSensorEnabled(SensitiveSource.Id, true);
        var after = await demo.PreviewAsync();

        Assert.Equal("Sensitive", after[SensitiveSource.Id]);
        Assert.Equal(1, source.ReadCount);
    }

    private sealed class SensitiveSource : ISensorSource
    {
        public const string Id = "demo_sensitive";

        public int ReadCount { get; private set; }

        public IReadOnlyList<SensorDefinition> Definitions { get; } =
            [new(Id, "Sensitive", "Test sensor.", SensorPrivacy.Sensitive, false)];

        public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context)
        {
            // Models the Windows sources: whatever is requested is collected, so
            // the catalog is what must keep a sensitive value out of the request.
            if (!enabled.Contains(Id)) return [];

            ReadCount++;
            return
            [
                new()
                {
                    UniqueId = Id,
                    Name = "Sensitive",
                    State = "Sensitive"
                }
            ];
        }

        public void Start(Action onChanged)
        {
        }

        public void Stop()
        {
        }
    }

    private sealed class CountingSource : ISensorSource
    {
        public const string PrimaryId = "demo_primary";
        public const string SecondaryId = "demo_secondary";

        public int StartCount { get; private set; }

        public bool IsRunning { get; private set; }

        public IReadOnlyList<SensorDefinition> Definitions { get; } =
        [
            new(PrimaryId, "Primary", "Test sensor.", SensorPrivacy.Benign, false),
            new(SecondaryId, "Secondary", "Test sensor.", SensorPrivacy.Benign, false)
        ];

        public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context) =>
            Definitions
                .Where(definition => enabled.Contains(definition.UniqueId))
                .Select(definition => new Sensor
                {
                    UniqueId = definition.UniqueId,
                    Name = definition.Name,
                    State = definition.Name
                })
                .ToList();

        public void Start(Action onChanged)
        {
            StartCount++;
            IsRunning = true;
        }

        public void Stop() => IsRunning = false;
    }
}
