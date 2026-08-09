namespace WindowsCompanion.Core.Sensors;

public enum NotificationState
{
    Unknown = 0,
    NotPresent = 1,
    Busy = 2,
    RunningDirect3DFullScreen = 3,
    PresentationMode = 4,
    AcceptsNotifications = 5,
    QuietTime = 6,
    App = 7
}

/// <summary>
/// Describes the shell's notification state.
/// </summary>
/// <remarks>
/// This is <c>SHQueryUserNotificationState</c>, and it is narrower than it
/// sounds. It reports presentation mode, exclusive full-screen apps, the
/// "busy"/app states, the lock screen, and the legacy quiet-time window that
/// follows a new user's first sign-in. It does <em>not</em> reflect the
/// Windows 11 Focus / Do Not Disturb toggle: switching Do Not Disturb on leaves
/// the value at <see cref="NotificationState.AcceptsNotifications"/>.
///
/// Windows exposes no supported API for the current Focus / Do Not Disturb state
/// to an unpackaged desktop app; the only known routes are undocumented WNF state
/// names and registry keys that shift between builds. Rather than ship a second
/// entity that would silently be wrong, the companion reports this state
/// accurately, says so in the sensor description, and exposes a derived
/// <c>suppresses_notifications</c> attribute for automations.
/// </remarks>
public static class NotificationStateFormatter
{
    public static string Describe(NotificationState state) => state switch
    {
        NotificationState.NotPresent => "Not Present",
        NotificationState.Busy => "Busy",
        NotificationState.RunningDirect3DFullScreen => "Full Screen",
        NotificationState.PresentationMode => "Presentation",
        NotificationState.AcceptsNotifications => "Accepts Notifications",
        NotificationState.QuietTime => "Quiet Time",
        NotificationState.App => "App",
        _ => "Unknown"
    };

    /// <summary>
    /// Whether Windows is withholding toasts for this reason. Unknown counts as
    /// "not suppressing": the shell delivers notifications unless it has a reason
    /// not to.
    /// </summary>
    public static bool SuppressesNotifications(NotificationState state) => state
        is NotificationState.NotPresent
        or NotificationState.Busy
        or NotificationState.RunningDirect3DFullScreen
        or NotificationState.PresentationMode
        or NotificationState.QuietTime;

    public static IDictionary<string, object> BuildAttributes(NotificationState state) =>
        new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["suppresses_notifications"] = SuppressesNotifications(state),
            // Stated explicitly so an automation author does not assume otherwise.
            ["includes_do_not_disturb"] = false
        };
}
