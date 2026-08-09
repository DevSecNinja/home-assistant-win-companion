namespace WindowsCompanion.Core.Lifecycle;

/// <summary>
/// What the machine is doing, or about to do, from the companion's point of view.
/// </summary>
/// <remarks>
/// Windows does not expose every distinction it appears to. Before the transition
/// happens there is no supported desktop API that separates sleep from hibernate,
/// nor shutdown from restart: both pairs arrive as the same notification. Those
/// values therefore exist in the model - a caller that learns the difference from
/// somewhere else can report it - but the Windows signal mapper never invents them.
/// See <c>docs/windows-lifecycle-signals.md</c>.
/// </remarks>
public enum LifecycleTransition
{
    /// <summary>The machine is up and this session is alive.</summary>
    Running = 0,

    /// <summary>Suspending to RAM. Also reported for hibernate, which is indistinguishable.</summary>
    Sleeping = 1,

    /// <summary>Suspending to disk, when something other than Windows tells us so.</summary>
    Hibernating = 2,

    /// <summary>The current user's session is ending, but the machine stays up.</summary>
    SigningOut = 3,

    /// <summary>Windows is shutting down. Also reported for restart, which is indistinguishable.</summary>
    ShuttingDown = 4,

    /// <summary>Windows is restarting, when something other than Windows tells us so.</summary>
    Restarting = 5
}

/// <summary>Renders a <see cref="LifecycleTransition"/> as a Home Assistant state.</summary>
public static class LifecycleStateFormatter
{
    /// <summary>
    /// Lower-case and underscore-separated: this is an enumerated state that
    /// automations compare against, not a label a person reads.
    /// </summary>
    public static string Describe(LifecycleTransition transition) => transition switch
    {
        LifecycleTransition.Sleeping => "sleeping",
        LifecycleTransition.Hibernating => "hibernating",
        LifecycleTransition.SigningOut => "signing_out",
        LifecycleTransition.ShuttingDown => "shutting_down",
        LifecycleTransition.Restarting => "restarting",
        _ => "running"
    };

    public static string IconFor(LifecycleTransition transition) => transition switch
    {
        LifecycleTransition.Sleeping => "mdi:sleep",
        LifecycleTransition.Hibernating => "mdi:snowflake",
        LifecycleTransition.SigningOut => "mdi:logout",
        LifecycleTransition.ShuttingDown => "mdi:power",
        LifecycleTransition.Restarting => "mdi:restart",
        _ => "mdi:desktop-tower-monitor"
    };

    /// <summary>
    /// How final a transition is. A machine that is both suspending and shutting
    /// down is shutting down: Windows can deliver both, in either order.
    /// </summary>
    public static int Severity(LifecycleTransition transition) => transition switch
    {
        LifecycleTransition.Running => 0,
        LifecycleTransition.Sleeping or LifecycleTransition.Hibernating => 1,
        LifecycleTransition.SigningOut => 2,
        _ => 3
    };
}
