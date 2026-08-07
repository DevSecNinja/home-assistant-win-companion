namespace HaCompanion.Core.Models;

/// <summary>
/// A sensor this installation has registered with Home Assistant.
/// </summary>
/// <remarks>
/// Persisted so the app remembers across restarts what Home Assistant knows about.
/// Without that memory a sensor removed from the app in a later version becomes an
/// orphan: Home Assistant keeps the entity and shows its last value forever, because
/// nothing ever tells it the sensor is gone.
///
/// The type and name are kept because disabling goes through <c>register_sensor</c>,
/// whose schema requires both.
/// </remarks>
public sealed class RegisteredSensor
{
    public string Type { get; set; } = "sensor";

    public string Name { get; set; } = string.Empty;
}
