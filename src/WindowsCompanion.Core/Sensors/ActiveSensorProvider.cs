using WindowsCompanion.Core.Models;

namespace WindowsCompanion.Core.Sensors;

/// <summary>
/// Maps an <see cref="ActiveState"/> onto the Home Assistant sensors. Pure and
/// fully unit-testable, like <see cref="BatterySensorProvider"/>.
/// </summary>
public static class ActiveSensorProvider
{
    public const string ActiveId = "active";
    public const string ScreenLockedId = "screen_locked";

    public static Sensor BuildActive(ActiveState state) => new()
    {
        UniqueId = ActiveId,
        Type = "binary_sensor",
        Name = "Active",
        State = state.IsActive,
        Icon = state.IsActive ? "mdi:monitor" : "mdi:monitor-off",
        Attributes = state.ToAttributes()
    };

    public static Sensor BuildScreenLocked(ActiveState state) => new()
    {
        UniqueId = ScreenLockedId,
        Type = "binary_sensor",
        Name = "Screen Locked",
        State = state.Locked,
        Icon = state.Locked ? "mdi:lock" : "mdi:lock-open-variant"
    };

    public static IReadOnlyList<Sensor> BuildAll(ActiveState state, IReadOnlySet<string> enabled)
    {
        var readings = new List<Sensor>();
        if (enabled.Contains(ActiveId)) readings.Add(BuildActive(state));
        if (enabled.Contains(ScreenLockedId)) readings.Add(BuildScreenLocked(state));
        return readings;
    }
}
