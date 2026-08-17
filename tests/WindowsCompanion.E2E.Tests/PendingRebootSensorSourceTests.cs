using WindowsCompanion.Core.Sensors;
using WindowsCompanion.E2E.Tests.Fixtures;
using WindowsCompanion_App.Services;

namespace WindowsCompanion.E2E.Tests;

[Collection(CompanionJourneyCollection.Name)]
public sealed class PendingRebootSensorSourceTests
{
    private static readonly HashSet<string> Enabled =
        new(StringComparer.Ordinal) { PendingRebootSensorSource.PendingRebootId };

    [Fact]
    public void Read_before_the_source_is_started_reports_the_default_not_pending_state()
    {
        // Read() only filters on the enabled set, matching DiskUsageSensorSource: it
        // never checks whether Start() ran. Before the first poll tick lands, the
        // change gate still holds its seeded "not pending" default rather than the
        // injected reader's value.
        var source = new PendingRebootSensorSource(() => new PendingRebootState(true, false, false));

        var sensor = Assert.Single(source.Read(Enabled, SensorReadContext.Periodic));

        Assert.Equal(false, sensor.State);
    }

    [Fact]
    public void Read_returns_nothing_when_the_sensor_is_disabled()
    {
        var source = new PendingRebootSensorSource(() => new PendingRebootState(true, false, false));

        var sensors = source.Read(new HashSet<string>(StringComparer.Ordinal), SensorReadContext.Periodic);

        Assert.Empty(sensors);
    }

    [Fact]
    public async Task Preview_reports_the_current_state_without_starting_the_poller()
    {
        var source = new PendingRebootSensorSource(() => new PendingRebootState(false, true, false));

        var sensors = await source.PreviewAsync(Enabled);

        var sensor = Assert.Single(sensors);
        Assert.Equal(PendingRebootSensorSource.PendingRebootId, sensor.UniqueId);
        Assert.Equal(true, sensor.State);
        Assert.Equal("binary_sensor", sensor.Type);
        Assert.Equal("problem", sensor.DeviceClass);
    }

    [Fact]
    public async Task Starting_seeds_the_state_so_a_subsequent_read_reflects_it()
    {
        var source = new PendingRebootSensorSource(
            () => new PendingRebootState(true, false, false),
            pollInterval: TimeSpan.FromMinutes(30));

        source.Start(() => { });
        try
        {
            // The loop's own first tick runs asynchronously; RefreshAsync joins it.
            await source.RefreshAsync();

            var sensor = Assert.Single(source.Read(Enabled, SensorReadContext.Periodic));
            Assert.Equal(true, sensor.State);
        }
        finally
        {
            source.Stop();
        }
    }

    [Fact]
    public async Task Refreshing_with_an_unchanged_state_does_not_notify()
    {
        var notified = 0;
        var source = new PendingRebootSensorSource(
            () => PendingRebootState.None,
            pollInterval: TimeSpan.FromMinutes(30));

        source.Start(() => notified++);
        try
        {
            await source.RefreshAsync();
            await source.RefreshAsync();

            Assert.Equal(0, notified);
        }
        finally
        {
            source.Stop();
        }
    }

    [Fact]
    public async Task Stopping_and_restarting_re_seeds_without_leaking_a_poller()
    {
        var state = new PendingRebootState(false, false, true);
        var source = new PendingRebootSensorSource(() => state, pollInterval: TimeSpan.FromMinutes(30));

        source.Start(() => { });
        await source.RefreshAsync();
        source.Stop();

        state = PendingRebootState.None;
        source.Start(() => { });
        try
        {
            await source.RefreshAsync();

            var sensor = Assert.Single(source.Read(Enabled, SensorReadContext.Periodic));
            Assert.Equal(false, sensor.State);
        }
        finally
        {
            source.Stop();
        }
    }
}
