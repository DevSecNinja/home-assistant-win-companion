namespace WindowsCompanion.Core.Lifecycle;

/// <summary>The effect of feeding one signal to a <see cref="LifecycleTracker"/>.</summary>
/// <param name="Changed">The tracked state actually moved.</param>
/// <param name="RequiresFinalPush">
/// The machine is on its way out, so this is the last chance to tell Home Assistant
/// anything.
/// </param>
public readonly record struct LifecycleObservation(bool Changed, bool RequiresFinalPush);

/// <summary>
/// Turns a stream of Windows notifications into a single coherent lifecycle state.
/// </summary>
/// <remarks>
/// Windows produces overlapping and repeated notifications: a shutdown delivers both
/// <c>WM_QUERYENDSESSION</c> and <c>WM_ENDSESSION</c>, <c>SystemEvents.SessionEnding</c>
/// arrives for the same event, and a suspend broadcast can follow a shutdown that is
/// already under way. Applying only the more final transition makes all of that
/// idempotent, and stops a late suspend from downgrading "shutting down" to
/// "sleeping" - which would be the last thing Home Assistant ever heard.
///
/// Anything that says we are running - a resume, or a shutdown another application
/// vetoed - always wins, because it can only be observed by a process that is
/// demonstrably still alive.
/// </remarks>
public sealed class LifecycleTracker
{
    public LifecycleTransition Current { get; private set; } = LifecycleTransition.Running;

    public string Reason { get; private set; } = "Startup";

    public bool Critical { get; private set; }

    /// <summary>When the current state was observed. Null until anything happens.</summary>
    public DateTimeOffset? ChangedAt { get; private set; }

    public LifecycleObservation Observe(LifecycleSignal signal, DateTimeOffset at)
    {
        var running = signal.Transition == LifecycleTransition.Running;
        var moreFinal = LifecycleStateFormatter.Severity(signal.Transition)
                        > LifecycleStateFormatter.Severity(Current);

        if (!running && !moreFinal) return new LifecycleObservation(false, false);
        if (running && Current == LifecycleTransition.Running) return new LifecycleObservation(false, false);

        Current = signal.Transition;
        Reason = signal.Reason;
        Critical = signal.Critical;
        ChangedAt = at;

        return new LifecycleObservation(true, !running);
    }
}
