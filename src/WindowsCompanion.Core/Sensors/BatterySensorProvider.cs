using WindowsCompanion.Core.Models;

namespace WindowsCompanion.Core.Sensors;

/// <summary>
/// Maps an OS <see cref="SystemStatus"/> snapshot into the Home Assistant
/// battery-level and battery-state sensors. Pure and fully unit-testable.
/// </summary>
public static class BatterySensorProvider
{
    public const string BatteryLevelId = "battery_level";
    public const string BatteryStateId = "battery_state";

    public static Sensor BuildBatteryLevel(SystemStatus status)
    {
        var level = status.HasBattery ? Math.Clamp(status.BatteryPercent, 0, 100) : 100;
        return new Sensor
        {
            UniqueId = BatteryLevelId,
            Type = "sensor",
            Name = "Battery Level",
            State = level,
            DeviceClass = "battery",
            UnitOfMeasurement = "%",
            StateClass = "measurement",
            EntityCategory = "diagnostic",
            Icon = BatteryIcon(status)
        };
    }

    public static Sensor BuildBatteryState(SystemStatus status)
    {
        var state = status.HasBattery ? status.BatteryStateString : "plugged in";
        return new Sensor
        {
            UniqueId = BatteryStateId,
            Type = "sensor",
            Name = "Battery State",
            State = state,
            DeviceClass = "enum",
            EntityCategory = "diagnostic",
            Icon = BatteryIcon(status),
            // Windows can report AC online while the charging bit is clear - adaptive
            // or conservation charging, or a thermal pause - which shows up as
            // "not charging" even though the machine is plugged in. Exposing mains
            // status separately gives automations a signal that does not flap.
            Attributes = new Dictionary<string, object>
            {
                ["ac_online"] = status.PowerState is PowerState.Charging or PowerState.Full
                    or PowerState.NotCharging or PowerState.PluggedIn,
                ["has_battery"] = status.HasBattery
            }
        };
    }

    public static IReadOnlyList<Sensor> BuildAll(SystemStatus status) =>
        new[] { BuildBatteryLevel(status), BuildBatteryState(status) };

    private static string BatteryIcon(SystemStatus status)
    {
        if (!status.HasBattery) return "mdi:power-plug";
        return status.PowerState is PowerState.Charging or PowerState.PluggedIn or PowerState.Full
            ? "mdi:battery-charging"
            : "mdi:battery";
    }
}
