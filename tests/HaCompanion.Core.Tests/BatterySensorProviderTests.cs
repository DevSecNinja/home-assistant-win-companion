using HaCompanion.Core.Models;
using HaCompanion.Core.Sensors;
using Xunit;

namespace HaCompanion.Core.Tests;

public class BatterySensorProviderTests
{
    [Fact]
    public void Discharging_laptop_reports_level_and_state()
    {
        var status = new SystemStatus(HasBattery: true, BatteryPercent: 42, PowerState.Discharging);

        var level = BatterySensorProvider.BuildBatteryLevel(status);
        var state = BatterySensorProvider.BuildBatteryState(status);

        Assert.Equal(42, level.State);
        Assert.Equal("%", level.UnitOfMeasurement);
        Assert.Equal("battery", level.DeviceClass);
        Assert.Equal("discharging", state.State);
        Assert.Equal("mdi:battery", level.Icon);
    }

    [Fact]
    public void Charging_uses_charging_icon()
    {
        var status = new SystemStatus(true, 80, PowerState.Charging);

        var level = BatterySensorProvider.BuildBatteryLevel(status);
        var state = BatterySensorProvider.BuildBatteryState(status);

        Assert.Equal("charging", state.State);
        Assert.Equal("mdi:battery-charging", level.Icon);
    }

    [Fact]
    public void Desktop_without_battery_is_reported_gracefully()
    {
        var status = new SystemStatus(HasBattery: false, BatteryPercent: 255, PowerState.Unknown);

        var level = BatterySensorProvider.BuildBatteryLevel(status);
        var state = BatterySensorProvider.BuildBatteryState(status);

        Assert.Equal(100, level.State);
        Assert.Equal("plugged in", state.State);
        Assert.Equal("mdi:power-plug", level.Icon);
    }

    [Fact]
    public void Battery_percent_is_clamped_to_valid_range()
    {
        var status = new SystemStatus(true, 250, PowerState.Discharging);

        var level = BatterySensorProvider.BuildBatteryLevel(status);

        Assert.Equal(100, level.State);
    }

    [Fact]
    public void BuildAll_returns_both_sensors()
    {
        var sensors = BatterySensorProvider.BuildAll(new SystemStatus(true, 50, PowerState.Charging));

        Assert.Equal(2, sensors.Count);
        Assert.Contains(sensors, s => s.UniqueId == BatterySensorProvider.BatteryLevelId);
        Assert.Contains(sensors, s => s.UniqueId == BatterySensorProvider.BatteryStateId);
    }
}
