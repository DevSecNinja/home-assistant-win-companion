namespace WindowsCompanion.Core.Lifecycle;

/// <summary>
/// One observation from the operating system, before any deduplication.
/// </summary>
/// <param name="Transition">The state the machine is moving into.</param>
/// <param name="Reason">
/// The Windows notification this came from, e.g. "Suspend" or "Sign-out". Reported
/// as an attribute so an automation can tell an ordinary shutdown from a forced one.
/// </param>
/// <param name="Critical">
/// Windows is ending the session without giving applications the usual chance to
/// respond (<c>ENDSESSION_CRITICAL</c>, or a critical resume after power loss).
/// </param>
public readonly record struct LifecycleSignal(
    LifecycleTransition Transition,
    string Reason,
    bool Critical = false)
{
    public static LifecycleSignal Running(string reason) => new(LifecycleTransition.Running, reason);
}
