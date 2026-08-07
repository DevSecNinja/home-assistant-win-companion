using System.Runtime.InteropServices;
using HaCompanion.Core.Models;
using HaCompanion.Core.Sensors;
using Microsoft.Win32;

namespace HaCompanion_App.Services;

/// <summary>
/// Windows shim for the Active / Screen Locked sensors: subscribes to session and
/// power events, polls idle time, and hands the resulting <see cref="ActiveState"/>
/// to <see cref="ActiveSensorProvider"/>.
/// </summary>
/// <remarks>
/// This type deliberately contains no decisions - what counts as "active", how idle
/// time is computed and how the sensors are shaped all live in HaCompanion.Core
/// where they are unit tested. Everything here is OS plumbing.
///
/// Lock, sleep and fast-user-switch are event driven. Only idle needs polling, via
/// <c>GetLastInputInfo</c> - a trivial syscall - and the poll merely recomputes
/// state locally: a push happens only when the derived state actually changes.
/// </remarks>
public sealed class ActiveSensorSource : ISensorSource
{
    public const string ActiveId = ActiveSensorProvider.ActiveId;
    public const string ScreenLockedId = ActiveSensorProvider.ScreenLockedId;

    private readonly SensorPreferences _preferences;
    private readonly System.Timers.Timer _idleTimer = new(5000);

    private Action? _onChanged;
    private bool _observing;
    private ActiveState _state;

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

    public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context)
    {
        // The screensaver has no notification, so it is sampled at read time.
        var state = _state with { Screensaver = IsScreensaverRunning() };
        return ActiveSensorProvider.BuildAll(state, enabled);
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
        var updated = e.Reason switch
        {
            SessionSwitchReason.SessionLock => _state with { Locked = true },
            SessionSwitchReason.SessionUnlock => _state with { Locked = false },
            SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect
                => _state with { FastUserSwitched = true },
            SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect
                => _state with { FastUserSwitched = false },
            _ => _state
        };

        Apply(updated);
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        var updated = e.Mode switch
        {
            PowerModes.Suspend => _state with { Sleeping = true },
            PowerModes.Resume => _state with { Sleeping = false },
            _ => _state
        };

        Apply(updated);
    }

    private void CheckIdle() =>
        Apply(_state with
        {
            Idle = IdleTime.IsIdle(GetIdleTime(), _preferences.IdleThresholdSeconds)
        });

    /// <summary>Stores the new state and pushes only if something actually changed.</summary>
    private void Apply(ActiveState updated)
    {
        if (updated == _state) return;
        _state = updated;
        _onChanged?.Invoke();
    }

    private static TimeSpan GetIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;

        return IdleTime.Since(unchecked((uint)Environment.TickCount), info.dwTime);
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
