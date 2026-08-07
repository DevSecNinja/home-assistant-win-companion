using HaCompanion.Core.Sensors;
using Xunit;

namespace HaCompanion.Core.Tests;

public class ActiveStateTests
{
    [Fact]
    public void Default_state_is_active()
    {
        Assert.True(new ActiveState().IsActive);
    }

    [Theory]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, true, false)]
    [InlineData(false, false, false, false, true)]
    public void Any_single_away_reason_makes_it_inactive(
        bool idle, bool locked, bool screensaver, bool sleeping, bool switched)
    {
        var state = new ActiveState(idle, locked, screensaver, sleeping, switched);
        Assert.False(state.IsActive);
    }

    [Fact]
    public void Attributes_expose_every_sub_state()
    {
        var state = new ActiveState(Idle: true, Locked: true);
        var attributes = state.ToAttributes();

        // A single boolean cannot express "locked" versus "merely idle", so each
        // reason has to survive into Home Assistant separately.
        Assert.True((bool)attributes["Idle"]);
        Assert.True((bool)attributes["Locked"]);
        Assert.False((bool)attributes["Screensaver"]);
        Assert.False((bool)attributes["Sleeping"]);
        Assert.False((bool)attributes["Fast User Switched"]);
    }

    [Fact]
    public void Active_sensor_reports_state_icon_and_attributes()
    {
        var sensor = ActiveSensorProvider.BuildActive(new ActiveState(Locked: true));

        Assert.Equal("active", sensor.UniqueId);
        Assert.Equal("binary_sensor", sensor.Type);
        Assert.Equal(false, sensor.State);
        Assert.Equal("mdi:monitor-off", sensor.Icon);
        Assert.True((bool)sensor.Attributes!["Locked"]);
    }

    [Fact]
    public void Screen_locked_sensor_tracks_only_the_lock()
    {
        // Idle must not make the screen appear locked.
        var sensor = ActiveSensorProvider.BuildScreenLocked(new ActiveState(Idle: true));
        Assert.Equal(false, sensor.State);

        Assert.Equal(true, ActiveSensorProvider.BuildScreenLocked(new ActiveState(Locked: true)).State);
    }

    [Fact]
    public void BuildAll_returns_only_enabled_sensors()
    {
        var enabled = new HashSet<string> { ActiveSensorProvider.ScreenLockedId };
        var readings = ActiveSensorProvider.BuildAll(new ActiveState(), enabled);

        Assert.Equal(ActiveSensorProvider.ScreenLockedId, Assert.Single(readings).UniqueId);
    }
}
