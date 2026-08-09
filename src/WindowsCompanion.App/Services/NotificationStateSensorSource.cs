using System.Runtime.InteropServices;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;

namespace WindowsCompanion_App.Services;

/// <summary>
/// Windows shim for the Notification State sensor.
/// </summary>
/// <remarks>
/// <c>SHQueryUserNotificationState</c> is the only supported source for this, and
/// it deliberately does not cover the Windows 11 Focus / Do Not Disturb toggle -
/// see <see cref="NotificationStateFormatter"/>. The state is therefore described
/// as presentation/full-screen/busy context rather than as a do-not-disturb
/// signal, and no separate focus entity is published.
/// </remarks>
public sealed class NotificationStateSensorSource : ISensorSource
{
    public const string NotificationStateId = "user_notification_state";

    private readonly System.Timers.Timer _timer = new(TimeSpan.FromSeconds(10));
    private readonly object _gate = new();
    private Action? _onChanged;
    private NotificationState _lastState = NotificationState.Unknown;
    private bool _observing;

    public NotificationStateSensorSource()
    {
        _timer.AutoReset = true;
        _timer.Elapsed += (_, _) => Poll();
    }

    public IReadOnlyList<SensorDefinition> Definitions { get; } =
    [
        new(
            NotificationStateId,
            "Notification State",
            "Whether Windows considers this PC busy, presenting, full-screen or ready "
            + "for notifications. Windows 11's Focus / Do Not Disturb switch is not "
            + "included: Windows exposes no supported way to read it.",
            SensorPrivacy.Benign,
            EnabledByDefault: true,
            ResourceUsage: "Low. Checks Windows every 10 seconds. Sends an extra update only when "
                           + "the notification state changes.",
            AutomationIdea: "When Windows reports presenting, turn on a meeting indicator light.")
    ];

    public IReadOnlyList<Sensor> Read(
        IReadOnlySet<string> enabled, SensorReadContext context)
    {
        if (!enabled.Contains(NotificationStateId)) return [];

        var state = Query();

        return
        [
            new()
            {
                UniqueId = NotificationStateId,
                Type = "sensor",
                Name = "Notification State",
                State = NotificationStateFormatter.Describe(state),
                EntityCategory = "diagnostic",
                Icon = IconFor(state),
                Attributes = NotificationStateFormatter.BuildAttributes(state)
            }
        ];
    }

    public void Start(Action onChanged)
    {
        _onChanged = onChanged;
        if (_observing) return;

        lock (_gate) _lastState = Query();
        _timer.Start();
        _observing = true;
    }

    public void Stop()
    {
        if (!_observing) return;
        _timer.Stop();
        _observing = false;
    }

    private void Poll()
    {
        var current = Query();
        var changed = false;

        lock (_gate)
        {
            if (current != _lastState)
            {
                _lastState = current;
                changed = true;
            }
        }

        if (changed) _onChanged?.Invoke();
    }

    private static NotificationState Query()
    {
        var result = SHQueryUserNotificationState(out var state);
        return result >= 0 && Enum.IsDefined(state) ? state : NotificationState.Unknown;
    }

    private static string IconFor(NotificationState state) => state switch
    {
        NotificationState.AcceptsNotifications => "mdi:bell",
        NotificationState.PresentationMode => "mdi:presentation",
        NotificationState.QuietTime => "mdi:bell-off",
        NotificationState.RunningDirect3DFullScreen or NotificationState.Busy
            => "mdi:monitor-lock",
        _ => "mdi:bell-outline"
    };

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out NotificationState state);
}
