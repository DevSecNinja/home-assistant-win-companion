namespace WindowsCompanion.Core.Sensors;

/// <summary>
/// Keeps the reported boot time stable.
/// </summary>
/// <remarks>
/// Boot time derived from a tick count drifts by milliseconds on every read. A
/// timestamp sensor that changes on every push would record a state change every
/// sync interval, filling Home Assistant's history with meaningless entries. So the
/// first value is cached and only replaced when it moves more than
/// <see cref="DriftToleranceSeconds"/> - which happens on a genuine reboot, or after
/// hibernation, since the tick count does not advance while the machine is asleep.
/// </remarks>
public sealed class BootTimeCalculator
{
    public const int DriftToleranceSeconds = 60;

    private DateTimeOffset? _bootTime;

    /// <summary>Returns a stable boot time given a freshly measured one.</summary>
    public DateTimeOffset Resolve(DateTimeOffset measured)
    {
        if (_bootTime is null
            || Math.Abs((measured - _bootTime.Value).TotalSeconds) > DriftToleranceSeconds)
        {
            _bootTime = TruncateToSecond(measured);
        }

        return _bootTime.Value;
    }

    private static DateTimeOffset TruncateToSecond(DateTimeOffset value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerSecond), value.Offset);
}
