using HaCompanion.Core.Models;

namespace HaCompanion.Core.Sensors;

/// <summary>
/// Reports why the companion last pushed to Home Assistant, mirroring the official
/// apps' <c>last_update_trigger</c> sensor. The state is the trigger reason rather
/// than a timestamp, because Home Assistant already records when an entity last
/// updated - the reason is the part it cannot know.
/// </summary>
/// <remarks>
/// The value is produced as part of the outgoing batch, so it never causes a push
/// of its own (which would otherwise feed back into itself indefinitely).
/// </remarks>
public sealed class LastUpdateSensorSource : ISensorSource
{
    public const string LastUpdateTriggerId = "last_update_trigger";

    public IReadOnlyList<SensorDefinition> Definitions { get; } = new[]
    {
        new SensorDefinition(
            LastUpdateTriggerId,
            "Last Update Trigger",
            "Why this PC last reported to Home Assistant. Useful for diagnosing a companion that has stopped reporting.",
            SensorPrivacy.Benign,
            EnabledByDefault: true)
    };

    public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context)
    {
        if (!enabled.Contains(LastUpdateTriggerId)) return Array.Empty<Sensor>();

        return new[]
        {
            new Sensor
            {
                UniqueId = LastUpdateTriggerId,
                Type = "sensor",
                Name = "Last Update Trigger",
                State = context.Reason,
                EntityCategory = "diagnostic",
                Icon = "mdi:laptop"
            }
        };
    }

    public void Start(Action onChanged) { }

    public void Stop() { }
}
