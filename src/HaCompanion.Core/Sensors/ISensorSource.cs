using HaCompanion.Core.Models;

namespace HaCompanion.Core.Sensors;

/// <summary>
/// Produces the current readings for one or more related sensors.
/// </summary>
/// <remarks>
/// A source is only asked for readings while at least one of its sensors is
/// enabled. Implementations that need an OS hook should register it in
/// <see cref="Start"/> and release it in <see cref="Stop"/> so that a disabled
/// sensor costs nothing at all, rather than merely being filtered out later.
/// </remarks>
public interface ISensorSource
{
    /// <summary>The sensors this source can produce.</summary>
    IReadOnlyList<SensorDefinition> Definitions { get; }

    /// <summary>
    /// Produces current readings. Only sensors whose ids appear in
    /// <paramref name="enabled"/> should be returned.
    /// </summary>
    IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context);

    /// <summary>
    /// Begins observing the underlying OS state. Called when the source's first
    /// sensor is enabled. <paramref name="onChanged"/> requests an immediate push.
    /// </summary>
    void Start(Action onChanged);

    /// <summary>Releases any OS hooks. Called when the last sensor is disabled.</summary>
    void Stop();
}
