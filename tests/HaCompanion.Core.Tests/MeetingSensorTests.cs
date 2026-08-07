using HaCompanion.Core.Models;
using HaCompanion.Core.Sensors;

namespace HaCompanion.Core.Tests;

public class MeetingSensorTests
{
    [Theory]
    [InlineData(NotificationState.NotPresent, "Not Present")]
    [InlineData(NotificationState.Busy, "Busy")]
    [InlineData(NotificationState.RunningDirect3DFullScreen, "Full Screen")]
    [InlineData(NotificationState.PresentationMode, "Presentation")]
    [InlineData(NotificationState.AcceptsNotifications, "Accepts Notifications")]
    [InlineData(NotificationState.QuietTime, "Quiet Time")]
    [InlineData(NotificationState.App, "App")]
    [InlineData(NotificationState.Unknown, "Unknown")]
    public void Notification_states_have_stable_display_values(
        NotificationState state, string expected)
    {
        Assert.Equal(expected, NotificationStateFormatter.Describe(state));
    }

    [Fact]
    public void Capability_is_active_while_any_entry_has_not_stopped()
    {
        Assert.True(CapabilityActivity.IsActive([133000000000000000, 0]));
        Assert.True(CapabilityActivity.IsActive([-1]));
    }

    [Fact]
    public void Capability_is_inactive_without_an_open_entry()
    {
        Assert.False(CapabilityActivity.IsActive([]));
        Assert.False(CapabilityActivity.IsActive([null, 133000000000000000]));
    }

    [Theory]
    [InlineData("Headset (Jabra Evolve2 65)")]
    [InlineData("Headphones (AirPods Pro)")]
    [InlineData("Poly Blackwire 5220 Series")]
    [InlineData("USB Earbuds")]
    public void Headset_names_are_recognized(string name)
    {
        Assert.True(HeadsetClassifier.IsHeadset(name));
    }

    [Theory]
    [InlineData("Speakers (Realtek(R) Audio)")]
    [InlineData("DELL U2723QE")]
    [InlineData("")]
    public void Non_headset_outputs_are_not_misclassified(string name)
    {
        Assert.False(HeadsetClassifier.IsHeadset(name));
    }

    [Fact]
    public async Task Catalog_uses_async_preview_without_starting_the_source()
    {
        var source = new AsyncPreviewSource();
        var catalog = new SensorCatalog([source], new SensorPreferences());

        var preview = await catalog.PreviewAsync();

        Assert.Equal("Ready", preview["async_preview"]);
        Assert.False(source.Started);
    }

    [Fact]
    public void Shared_source_runs_until_its_last_sensor_is_disabled()
    {
        var preferences = new SensorPreferences();
        var source = new TrackingSource();
        var catalog = new SensorCatalog([source], preferences);
        catalog.Start(() => { });

        Assert.Equal(0, source.StartCount);

        catalog.SetEnabled("first", true);
        catalog.SetEnabled("second", true);
        Assert.Equal(1, source.StartCount);

        catalog.SetEnabled("first", false);
        Assert.Equal(0, source.StopCount);

        catalog.SetEnabled("second", false);
        Assert.Equal(1, source.StopCount);
    }

    private sealed class AsyncPreviewSource : ISensorSource
    {
        public bool Started { get; private set; }

        public IReadOnlyList<SensorDefinition> Definitions { get; } =
        [
            new("async_preview", "Async Preview", "Test sensor.",
                SensorPrivacy.Benign, EnabledByDefault: false)
        ];

        public IReadOnlyList<Sensor> Read(
            IReadOnlySet<string> enabled, SensorReadContext context) => [];

        public ValueTask<IReadOnlyList<Sensor>> PreviewAsync(
            IReadOnlySet<string> requested,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Sensor>>(
            [
                new()
                {
                    UniqueId = "async_preview",
                    Name = "Async Preview",
                    State = "Ready"
                }
            ]);

        public void Start(Action onChanged) => Started = true;

        public void Stop() => Started = false;
    }

    private sealed class TrackingSource : ISensorSource
    {
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public IReadOnlyList<SensorDefinition> Definitions { get; } =
        [
            new("first", "First", "First sensor.", SensorPrivacy.Benign, false),
            new("second", "Second", "Second sensor.", SensorPrivacy.Benign, false)
        ];

        public IReadOnlyList<Sensor> Read(
            IReadOnlySet<string> enabled, SensorReadContext context) => [];

        public void Start(Action onChanged) => StartCount++;

        public void Stop() => StopCount++;
    }
}
