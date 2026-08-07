namespace HaCompanion.Core.Models;

/// <summary>Coarse power state derived from the OS power status.</summary>
public enum PowerState
{
    Unknown,
    Charging,
    Discharging,
    Full,
    NotCharging,
    PluggedIn
}

/// <summary>A snapshot of the machine's power/battery status.</summary>
public sealed record SystemStatus(bool HasBattery, int BatteryPercent, PowerState PowerState)
{
    /// <summary>The HA "battery_state" enum string for this status.</summary>
    public string BatteryStateString => PowerState switch
    {
        PowerState.Charging => "charging",
        PowerState.Discharging => "discharging",
        PowerState.Full => "full",
        PowerState.NotCharging => "not_charging",
        PowerState.PluggedIn => "plugged in",
        _ => "unavailable"
    };
}
