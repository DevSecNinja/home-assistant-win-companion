namespace WindowsCompanion.Core.Lifecycle;

/// <summary>
/// Translates raw Windows notifications into <see cref="LifecycleSignal"/>s.
/// </summary>
/// <remarks>
/// The Windows constants are plain integers, so the whole translation lives here in
/// Core where it is unit tested against the documented values, and the App project
/// only has to hand over the numbers it received. Anything not recognised maps to
/// <c>null</c>: a lifecycle source must stay silent rather than guess.
///
/// Session lock, unlock and fast user switching are deliberately *not* mapped. They
/// already drive the Active and Screen Locked sensors, and reporting them here as
/// well would produce two entities disagreeing about the same fact.
/// </remarks>
public static class WindowsLifecycleMessages
{
    public const uint WM_QUERYENDSESSION = 0x0011;
    public const uint WM_ENDSESSION = 0x0016;
    public const uint WM_POWERBROADCAST = 0x0218;

    public const int PBT_APMSUSPEND = 0x0004;
    public const int PBT_APMRESUMECRITICAL = 0x0006;
    public const int PBT_APMRESUMESUSPEND = 0x0007;
    public const int PBT_APMRESUMEAUTOMATIC = 0x0012;
    public const int PBT_POWERSETTINGCHANGE = 0x8013;

    public const int ENDSESSION_CLOSEAPP = 0x00000001;
    public const int ENDSESSION_CRITICAL = 0x40000000;
    public const long ENDSESSION_LOGOFF = 0x80000000;

    /// <summary><c>Microsoft.Win32.PowerModes.Suspend</c>.</summary>
    public const int PowerModeSuspend = 4;

    /// <summary><c>Microsoft.Win32.PowerModes.Resume</c>.</summary>
    public const int PowerModeResume = 7;

    /// <summary><c>Microsoft.Win32.SessionEndReasons.Logoff</c>.</summary>
    public const int SessionEndReasonLogoff = 1;

    /// <summary><c>Microsoft.Win32.SessionEndReasons.SystemShutdown</c>.</summary>
    public const int SessionEndReasonSystemShutdown = 2;

    /// <summary><c>Microsoft.Win32.SessionSwitchReason.SessionLogoff</c>.</summary>
    public const int SessionSwitchLogoff = 6;

    /// <summary>
    /// Maps a window message. <paramref name="lParam"/> is only meaningful for the
    /// end-session messages and is ignored otherwise.
    /// </summary>
    public static LifecycleSignal? MapWindowMessage(uint message, nint wParam, nint lParam) => message switch
    {
        WM_POWERBROADCAST => MapPowerBroadcast((int)wParam),

        // Answering this message is a vote, and we always vote yes; the mapping is
        // only about what to report. See the source in the App project.
        WM_QUERYENDSESSION => MapEndSessionFlags(lParam),

        // wParam FALSE means another application vetoed the shutdown after we were
        // told it was happening, so the machine is staying up.
        WM_ENDSESSION => wParam == 0
            ? LifecycleSignal.Running("Session end cancelled")
            : MapEndSessionFlags(lParam),

        _ => null
    };

    public static LifecycleSignal? MapPowerBroadcast(int eventType) => eventType switch
    {
        PBT_APMSUSPEND => new LifecycleSignal(LifecycleTransition.Sleeping, "Suspend"),
        PBT_APMRESUMESUSPEND => LifecycleSignal.Running("Resume"),
        PBT_APMRESUMEAUTOMATIC => LifecycleSignal.Running("Automatic resume"),
        PBT_APMRESUMECRITICAL => new LifecycleSignal(LifecycleTransition.Running, "Critical resume", Critical: true),
        _ => null
    };

    /// <summary>
    /// Maps the <c>ENDSESSION_*</c> flags carried by both end-session messages.
    /// Without <c>ENDSESSION_LOGOFF</c> the session is ending because the machine
    /// is going down - Windows does not say whether it will come back up, so a
    /// restart is indistinguishable from a shutdown here.
    /// </summary>
    public static LifecycleSignal MapEndSessionFlags(nint flags)
    {
        var critical = ((long)flags & ENDSESSION_CRITICAL) != 0;

        if (((long)flags & ENDSESSION_LOGOFF) != 0)
            return new LifecycleSignal(LifecycleTransition.SigningOut, "Sign-out", critical);

        return new LifecycleSignal(
            LifecycleTransition.ShuttingDown,
            critical ? "Critical shutdown" : "Shutdown or restart",
            critical);
    }

    /// <summary>Maps <c>SystemEvents.PowerModeChanged</c>, the managed equivalent path.</summary>
    public static LifecycleSignal? MapPowerMode(int powerMode) => powerMode switch
    {
        PowerModeSuspend => new LifecycleSignal(LifecycleTransition.Sleeping, "Suspend"),
        PowerModeResume => LifecycleSignal.Running("Resume"),
        _ => null
    };

    /// <summary>Maps <c>SystemEvents.SessionEnding</c> / <c>SessionEnded</c>.</summary>
    public static LifecycleSignal? MapSessionEndReason(int reason) => reason switch
    {
        SessionEndReasonLogoff => new LifecycleSignal(LifecycleTransition.SigningOut, "Sign-out"),
        SessionEndReasonSystemShutdown =>
            new LifecycleSignal(LifecycleTransition.ShuttingDown, "Shutdown or restart"),
        _ => null
    };

    /// <summary>
    /// Maps <c>SystemEvents.SessionSwitch</c>. Only sign-out is a lifecycle event;
    /// every other reason belongs to the Active and Screen Locked sensors.
    /// </summary>
    public static LifecycleSignal? MapSessionSwitch(int reason) => reason == SessionSwitchLogoff
        ? new LifecycleSignal(LifecycleTransition.SigningOut, "Session sign-out")
        : null;
}
