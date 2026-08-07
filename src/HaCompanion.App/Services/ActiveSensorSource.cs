using System.Runtime.InteropServices;
using HaCompanion.Core.Models;
using HaCompanion.Core.Sensors;
using Microsoft.Win32;

namespace HaCompanion_App.Services;

/// <summary>
/// Reports whether the PC is actively in use, mirroring the macOS companion's
/// "Active" sensor: active means not idle, locked, screensavering, display-off,
/// asleep or fast-user-switched. Each sub-state is exposed as an attribute, and
/// "Screen Locked" is also surfaced as its own sensor because automating on an
/// entity state is far simpler than automating on an attribute.
/// </summary>
/// <remarks>
/// Lock, sleep and fast-user-switch are event driven. Only idle needs polling, via
/// <c>GetLastInputInfo</c> - a trivial syscall - and the poll merely recomputes
/// state locally: a push happens only when the derived state actually changes.
/// </remarks>
public sealed class ActiveSensorSource : ISensorSource
{
    public const string ActiveId = "active";
    public const string ScreenLockedId = "screen_locked";

    private readonly SensorPreferences _preferences;
    private readonly System.Timers.Timer _idleTimer = new(5000);

    private Action? _onChanged;
    private bool _observing;

    private bool _locked;
    private bool _sleeping;
    private bool _fastUserSwitched;
    private bool _idle;

    public ActiveSensorSource(SensorPreferences preferences)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _idleTimer.Elapsed += (_, _) => CheckIdle();
        _idleTimer.AutoReset = true;
    }

    public IReadOnlyList<SensorDefinition> Definitions { get; } = new[]
    {
        new SensorDefinition(
            ActiveId,
            "Active",
            "On while you are actively using this PC. Off when locked, asleep or idle.",
            SensorPrivacy.Benign,
            EnabledByDefault: true),
        new SensorDefinition(
            ScreenLockedId,
            "Screen Locked",
            "On while this PC's screen is locked.",
            SensorPrivacy.Benign,
            EnabledByDefault: true)
    };

    private bool IsActive => !_locked && !_sleeping && !_fastUserSwitched && !_idle && !IsScreensaverRunning();

    public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context)
    {
        var readings = new List<Sensor>();
        var screensaver = IsScreensaverRunning();

        if (enabled.Contains(ActiveId))
        {
            readings.Add(new Sensor
            {
                UniqueId = ActiveId,
                Type = "binary_sensor",
                Name = "Active",
                State = IsActive,
                Icon = IsActive ? "mdi:monitor" : "mdi:monitor-off",
                Attributes = new Dictionary<string, object>
                {
                    ["Idle"] = _idle,
                    ["Locked"] = _locked,
                    ["Screensaver"] = screensaver,
                    ["Sleeping"] = _sleeping,
                    ["Fast User Switched"] = _fastUserSwitched
                }
            });
        }

        if (enabled.Contains(ScreenLockedId))
        {
            readings.Add(new Sensor
            {
                UniqueId = ScreenLockedId,
                Type = "binary_sensor",
                Name = "Screen Locked",
                State = _locked,
                Icon = _locked ? "mdi:lock" : "mdi:lock-open-variant"
            });
        }

        return readings;
    }

    public void Start(Action onChanged)
    {
        _onChanged = onChanged;
        if (_observing) return;

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _idleTimer.Start();
        _observing = true;
    }

    public void Stop()
    {
        if (!_observing) return;

        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _idleTimer.Stop();
        _observing = false;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        var changed = e.Reason switch
        {
            SessionSwitchReason.SessionLock => Set(ref _locked, true),
            SessionSwitchReason.SessionUnlock => Set(ref _locked, false),
            SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect
                => Set(ref _fastUserSwitched, true),
            SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect
                => Set(ref _fastUserSwitched, false),
            _ => false
        };

        if (changed) _onChanged?.Invoke();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        var changed = e.Mode switch
        {
            PowerModes.Suspend => Set(ref _sleeping, true),
            PowerModes.Resume => Set(ref _sleeping, false),
            _ => false
        };

        if (changed) _onChanged?.Invoke();
    }

    private void CheckIdle()
    {
        var threshold = TimeSpan.FromSeconds(Math.Max(30, _preferences.IdleThresholdSeconds));
        var shouldBeIdle = GetIdleTime() >= threshold;
        if (Set(ref _idle, shouldBeIdle)) _onChanged?.Invoke();
    }

    private static bool Set(ref bool field, bool value)
    {
        if (field == value) return false;
        field = value;
        return true;
    }

    private static TimeSpan GetIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;

        // Both values are 32-bit millisecond tick counts and wrap every ~49 days;
        // unchecked subtraction stays correct across the wrap.
        var elapsed = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(elapsed);
    }

    private static bool IsScreensaverRunning()
    {
        var running = false;
        return SystemParametersInfo(SPI_GETSCREENSAVERRUNNING, 0, ref running, 0) && running;
    }

    private const uint SPI_GETSCREENSAVERRUNNING = 0x0072;

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint uiAction, uint uiParam, ref bool pvParam, uint fWinIni);
}
