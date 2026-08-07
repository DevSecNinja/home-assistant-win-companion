namespace HaCompanion.Core.Sensors;

/// <summary>
/// Context passed to sensor sources when readings are collected.
/// </summary>
/// <param name="Reason">
/// Why this collection is happening, e.g. "Registration", "Periodic",
/// "State Change" or "Settings Changed". Reported as-is by the
/// <c>last_update_trigger</c> diagnostic sensor, matching the official companion
/// apps: Home Assistant already records *when* an entity last updated, so the
/// useful thing to add is *why* the app phoned home.
/// </param>
public readonly record struct SensorReadContext(string Reason)
{
    public static SensorReadContext Registration => new("Registration");
    public static SensorReadContext Periodic => new("Periodic");
    public static SensorReadContext StateChange => new("State Change");
    public static SensorReadContext SettingsChanged => new("Settings Changed");
}
