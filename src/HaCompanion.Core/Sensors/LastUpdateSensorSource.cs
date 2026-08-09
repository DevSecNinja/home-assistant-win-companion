using HaCompanion.Core.Abstractions;
using HaCompanion.Core.Models;

namespace HaCompanion.Core.Sensors;

/// <summary>
/// Optionally reports when the companion last pushed to Home Assistant.
/// </summary>
/// <remarks>
/// Off by default. Home Assistant's built-in `last_reported` already tracks when an
/// entity last reported, without writing a history entry, whereas this sensor
/// changes on every update and so records one every sync interval. It exists for
/// people who want staleness to be obvious in the normal UI and accept that cost.
///
/// The value is produced as part of the outgoing batch, so it never causes a push
/// of its own (which would otherwise feed back into itself indefinitely).
/// </remarks>
public sealed class LastUpdateSensorSource : ISensorSource
{
    public const string LastUpdateTimeId = "last_update_time";

    private readonly IClock _clock;

    public LastUpdateSensorSource(IClock? clock = null) => _clock = clock ?? new SystemClock();

    public IReadOnlyList<SensorDefinition> Definitions { get; } = new[]
    {
        new SensorDefinition(
            LastUpdateTimeId,
            "Last Update Time",
            "When this PC last reported. Makes staleness obvious, but writes a history entry "
            + "every sync. Home Assistant's built-in last_reported already tracks this without "
            + "the recorder cost.",
            SensorPrivacy.Benign,
            EnabledByDefault: false,
            ResourceUsage: "Changes in every normal batch, creating a Home Assistant recorder entry "
                           + "about once per minute even when nothing else changed.")
    };

    public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context)
    {
        if (!enabled.Contains(LastUpdateTimeId)) return Array.Empty<Sensor>();

        return new[]
        {
            new Sensor
            {
                UniqueId = LastUpdateTimeId,
                Type = "sensor",
                Name = "Last Update Time",
                State = _clock.UtcNow.ToString("o"),
                DeviceClass = "timestamp",
                EntityCategory = "diagnostic",
                Icon = "mdi:clock-check-outline"
            }
        };
    }

    public void Start(Action onChanged) { }

    public void Stop() { }
}
