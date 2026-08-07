namespace HaCompanion.Core.Sensors;

/// <summary>
/// The user's per-sensor choices. Absent entries fall back to the sensor's
/// <see cref="SensorDefinition.EnabledByDefault"/>, so a newly added sensor never
/// silently switches itself on for an existing install if it is privacy-sensitive.
/// </summary>
public sealed class SensorPreferences
{
    /// <summary>Explicit user choices, keyed by sensor unique id.</summary>
    public Dictionary<string, bool> Enabled { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Seconds without input before the machine counts as idle.</summary>
    public int IdleThresholdSeconds { get; set; } = 300;

    public bool IsEnabled(SensorDefinition definition) =>
        Enabled.TryGetValue(definition.UniqueId, out var value) ? value : definition.EnabledByDefault;

    public void Set(string uniqueId, bool enabled) => Enabled[uniqueId] = enabled;
}
