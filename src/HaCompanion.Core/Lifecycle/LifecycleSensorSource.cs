using HaCompanion.Core.Models;
using HaCompanion.Core.Sensors;

namespace HaCompanion.Core.Lifecycle;

/// <summary>
/// Reports the machine's lifecycle state as a single enumerated sensor.
/// </summary>
/// <remarks>
/// One sensor rather than several: a timestamp entity would write a history row on
/// every transition without telling an automation anything the state and its
/// attributes do not, and folding this into the existing Active sensor would make
/// "off" mean four different things. The Active sensor keeps answering "is someone
/// using this PC"; this one answers "is this PC about to go away".
///
/// The transition that was never delivered stays in the attributes rather than in
/// the state, so an automation that reacts to <c>shutting_down</c> does not fire a
/// second time when the machine comes back and finally reports what happened.
/// </remarks>
public sealed class LifecycleSensorSource : ISensorSource
{
    public const string SystemStateId = "system_state";

    private readonly LifecycleCoordinator _coordinator;
    private readonly ILifecycleSignalSource _signals;

    private Action? _onChanged;
    private bool _observing;

    public LifecycleSensorSource(LifecycleCoordinator coordinator, ILifecycleSignalSource signals)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _signals = signals ?? throw new ArgumentNullException(nameof(signals));
    }

    public IReadOnlyList<SensorDefinition> Definitions { get; } = new[]
    {
        new SensorDefinition(
            SystemStateId,
            "System State",
            "Whether this PC is running, going to sleep, signing out or shutting down. "
            + "Best effort only: Windows may not notify the app before a suspend or "
            + "shutdown, so the final update may never reach Home Assistant, and sleep "
            + "cannot be told apart from hibernate or a shutdown from a restart. "
            + "Missed transitions are recorded locally and reported after the next "
            + "connection, so do not rely on this as the only trigger for a critical "
            + "automation.",
            SensorPrivacy.Benign,
            EnabledByDefault: false,
            ResourceUsage: "Low. Does not check repeatedly. May try one extra update before sleep, "
                           + "sign-out or shutdown, but delivery is not guaranteed.",
            AutomationIdea: "When sleeping is reported, turn off the desk lights (best effort).")
    };

    public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context)
    {
        if (!enabled.Contains(SystemStateId)) return Array.Empty<Sensor>();

        var current = _coordinator.Tracker.Current;
        var attributes = new Dictionary<string, object>
        {
            ["Reason"] = _coordinator.Tracker.Reason,
            ["Critical"] = _coordinator.Tracker.Critical
        };

        if (_coordinator.Tracker.ChangedAt is { } changedAt)
            attributes["Since"] = changedAt.ToString("o");

        var pending = _coordinator.Pending;
        if (pending is { Acknowledged: false } && !_coordinator.PendingIsCurrent)
        {
            // Observed before this machine came back, and never confirmed by Home
            // Assistant - which is the normal case for shutdown and suspend.
            attributes["Last Unreported Transition"] = LifecycleStateFormatter.Describe(pending.Transition);
            attributes["Last Unreported At"] = pending.ObservedAt.ToString("o");
            attributes["Last Unreported Reason"] = pending.Reason;
        }

        _coordinator.NoteRead();

        return new[]
        {
            new Sensor
            {
                UniqueId = SystemStateId,
                Type = "sensor",
                Name = "System State",
                State = LifecycleStateFormatter.Describe(current),
                Icon = LifecycleStateFormatter.IconFor(current),
                EntityCategory = "diagnostic",
                Attributes = attributes
            }
        };
    }

    public void Start(Action onChanged)
    {
        _onChanged = onChanged;
        if (_observing) return;

        _coordinator.Changed += OnCoordinatorChanged;
        _signals.SignalObserved += _coordinator.Observe;
        _coordinator.Start();
        _signals.Start();
        _observing = true;
    }

    public void Stop()
    {
        if (!_observing) return;

        _signals.Stop();
        _signals.SignalObserved -= _coordinator.Observe;
        _coordinator.Changed -= OnCoordinatorChanged;
        _coordinator.Stop();
        _observing = false;
    }

    private void OnCoordinatorChanged() => _onChanged?.Invoke();
}
