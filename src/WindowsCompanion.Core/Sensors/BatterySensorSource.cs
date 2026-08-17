using WindowsCompanion.Core.Abstractions;
using WindowsCompanion.Core.Models;

namespace WindowsCompanion.Core.Sensors;

/// <summary>
/// Exposes the battery sensors through the catalog. Reading power status is a
/// cheap syscall, so this source needs no OS hook.
/// </summary>
public sealed class BatterySensorSource : ISensorSource
{
    private readonly ISystemStatusProvider _status;

    public BatterySensorSource(ISystemStatusProvider status)
    {
        _status = status ?? throw new ArgumentNullException(nameof(status));
    }

    public IReadOnlyList<SensorDefinition> Definitions { get; } = new[]
    {
        new SensorDefinition(
            BatterySensorProvider.BatteryLevelId,
            "Battery Level",
            "Charge percentage of this PC's battery.",
            SensorPrivacy.Benign,
            EnabledByDefault: true,
            AutomationIdea: "When the battery drops below 20%, send a reminder to charge the PC."),
        new SensorDefinition(
            BatterySensorProvider.BatteryStateId,
            "Battery State",
            "Whether this PC is charging, discharging or plugged in.",
            SensorPrivacy.Benign,
            EnabledByDefault: true,
            AutomationIdea: "When charging starts, turn a desk indicator light green."),
        new SensorDefinition(
            BatterySensorProvider.AcPowerId,
            "AC Power",
            "Whether this PC is connected to mains power.",
            SensorPrivacy.Benign,
            EnabledByDefault: true,
            AutomationIdea: "When AC power is disconnected, notify that the laptop is now running on battery.")
    };

    public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context) =>
        BatterySensorProvider.BuildAll(_status.GetStatus())
            .Where(s => enabled.Contains(s.UniqueId))
            .ToList();

    public void Start(Action onChanged) { }

    public void Stop() { }
}
