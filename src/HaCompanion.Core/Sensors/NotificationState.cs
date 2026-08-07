namespace HaCompanion.Core.Sensors;

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
}
