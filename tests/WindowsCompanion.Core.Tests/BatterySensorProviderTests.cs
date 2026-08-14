using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;
using Xunit;

namespace WindowsCompanion.Core.Tests;

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

    [Theory]
    [InlineData(PowerState.Charging, true)]
    [InlineData(PowerState.Full, true)]
    [InlineData(PowerState.NotCharging, true)]
    [InlineData(PowerState.PluggedIn, true)]
    [InlineData(PowerState.Discharging, false)]
    public void AC_power_reflects_mains_connection_for_laptops(PowerState powerState, bool expectedAcOnline)
    {
        var status = new SystemStatus(HasBattery: true, BatteryPercent: 50, powerState);

        var acPower = BatterySensorProvider.BuildAcPower(status);
        var state = BatterySensorProvider.BuildBatteryState(status);

        Assert.Equal("binary_sensor", acPower.Type);
        Assert.Equal("plug", acPower.DeviceClass);
        Assert.Equal(expectedAcOnline, acPower.State);
        Assert.Equal(expectedAcOnline, state.Attributes!["ac_online"]);
        Assert.Equal(expectedAcOnline ? "mdi:power-plug" : "mdi:power-plug-off", acPower.Icon);
    }

    [Fact]
    public void Desktop_without_battery_reports_ac_power_connected()
    {
        var status = new SystemStatus(HasBattery: false, BatteryPercent: 255, PowerState.Unknown);

        var acPower = BatterySensorProvider.BuildAcPower(status);

        Assert.True((bool)acPower.State!);
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

        Assert.Equal(3, sensors.Count);
        Assert.Contains(sensors, s => s.UniqueId == BatterySensorProvider.BatteryLevelId);
        Assert.Contains(sensors, s => s.UniqueId == BatterySensorProvider.BatteryStateId);
        Assert.Contains(sensors, s => s.UniqueId == BatterySensorProvider.AcPowerId);
    }
}
