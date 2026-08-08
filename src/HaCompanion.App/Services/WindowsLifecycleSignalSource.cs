using System.Runtime.InteropServices;
using HaCompanion.Core.Lifecycle;
using Microsoft.Win32;

namespace HaCompanion_App.Services;

/// <summary>
/// Watches Windows for sleep, sign-out and shutdown, and forwards each notification
/// to <see cref="HaCompanion.Core.Lifecycle.WindowsLifecycleMessages"/> for mapping.
/// </summary>
/// <remarks>
/// Two independent paths feed the same mapper, because neither is sufficient on its
/// own:
///
/// * A dedicated hidden top-level window. <c>WM_QUERYENDSESSION</c> and
///   <c>WM_ENDSESSION</c> are only delivered to top-level windows, so a message-only
///   (<c>HWND_MESSAGE</c>) window would never see a shutdown at all. It is separate
///   from the WinUI window because this app lives in the tray: the main window is
///   routinely closed, and a hook on a window that no longer exists is a hook that
///   silently stops working.
/// * <c>SystemEvents</c>, which raises the same facts on its own hidden window.
///
/// Duplicates are expected and harmless - the tracker in Core applies each
/// transition once. The window runs on its own background thread with its own
/// message pump so that its lifetime does not depend on the UI thread being idle,
/// and everything the pump touches is created and destroyed on that thread.
///
/// The window procedure never blocks. <c>WM_QUERYENDSESSION</c> always answers TRUE:
/// the companion must not veto, delay or question a shutdown, and it never puts up
/// UI on this path.
/// </remarks>
public sealed class WindowsLifecycleSignalSource : ILifecycleSignalSource, IDisposable
{
    private const string WindowClassName = "HaCompanionLifecycleWindow";
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_DESTROY = 0x0002;

    private readonly object _gate = new();

    // Signals that the pump has either created its window or given up, so Stop can
    // always find a window to close rather than leaking a thread that starts a
    // moment later.
    private readonly ManualResetEventSlim _ready = new();

    // The delegate is handed to unmanaged code, so it must outlive the P/Invoke that
    // registered it; a local would be collected while Windows still holds the pointer.
    private readonly WndProc _wndProc;

    private Thread? _pump;
    private nint _hwnd;
    private bool _observing;

    public WindowsLifecycleSignalSource() => _wndProc = OnWindowMessage;

    public event Action<LifecycleSignal>? SignalObserved;

    public void Start()
    {
        lock (_gate)
        {
            if (_observing) return;
            _observing = true;

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionEnding += OnSessionEnding;
            SystemEvents.SessionSwitch += OnSessionSwitch;

            _pump = new Thread(PumpMessages)
            {
                Name = "HaCompanion lifecycle",
                IsBackground = true
            };
            _pump.SetApartmentState(ApartmentState.STA);
            _ready.Reset();
            _pump.Start();
        }

        _ready.Wait(TimeSpan.FromSeconds(2));
    }

    public void Stop()
    {
        Thread? pump;
        nint hwnd;

        lock (_gate)
        {
            if (!_observing) return;
            _observing = false;

            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionEnding -= OnSessionEnding;
            SystemEvents.SessionSwitch -= OnSessionSwitch;

            pump = _pump;
            hwnd = _hwnd;
            _pump = null;
        }

        // Destroying the window has to happen on the thread that created it, so ask
        // its own pump to do it and let the loop end naturally.
        if (hwnd != 0) PostMessage(hwnd, WM_CLOSE, 0, 0);
        pump?.Join(TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        Stop();
        _ready.Dispose();
    }

    private void PumpMessages()
    {
        var instance = GetModuleHandle(null);
        var windowClass = new WNDCLASS
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = instance,
            lpszClassName = WindowClassName
        };

        // A class survives for the lifetime of the process, so a restart of this
        // source finds it already registered. That is success, not failure.
        RegisterClass(ref windowClass);

        var hwnd = CreateWindowEx(
            0, WindowClassName, "HaCompanion Lifecycle", 0, 0, 0, 0, 0, 0, 0, instance, 0);
        _hwnd = hwnd;
        _ready.Set();
        if (hwnd == 0) return;

        while (GetMessage(out var message, 0, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

        _hwnd = 0;
    }

    private nint OnWindowMessage(nint hwnd, uint message, nint wParam, nint lParam)
    {
        Report(WindowsLifecycleMessages.MapWindowMessage(message, wParam, lParam));

        switch (message)
        {
            // Always consent. Reporting is someone else's problem, on another thread.
            case WindowsLifecycleMessages.WM_QUERYENDSESSION:
                return 1;

            case WindowsLifecycleMessages.WM_ENDSESSION:
                return 0;

            case WM_DESTROY:
                PostQuitMessage(0);
                return 0;

            default:
                return DefWindowProc(hwnd, message, wParam, lParam);
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) =>
        Report(WindowsLifecycleMessages.MapPowerMode((int)e.Mode));

    private void OnSessionEnding(object sender, SessionEndingEventArgs e)
    {
        // Never cancel: Cancel = true asks Windows to abandon the shutdown.
        e.Cancel = false;
        Report(WindowsLifecycleMessages.MapSessionEndReason((int)e.Reason));
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e) =>
        Report(WindowsLifecycleMessages.MapSessionSwitch((int)e.Reason));

    private void Report(LifecycleSignal? signal)
    {
        if (signal is not { } observed) return;

        try
        {
            SignalObserved?.Invoke(observed);
        }
        catch
        {
            // A handler must never be able to break the message pump, and on the
            // shutdown path it must never be able to hold Windows up.
        }
    }

    private delegate nint WndProc(nint hwnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int x;
        public int y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);
}
