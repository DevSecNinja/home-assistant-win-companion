using System.Runtime.InteropServices;
using WindowsCompanion.Core.Lifecycle;
using Microsoft.Win32;

namespace WindowsCompanion_App.Services;

/// <summary>
/// Watches Windows for sleep, sign-out and shutdown, and forwards each notification
/// to <see cref="WindowsCompanion.Core.Lifecycle.WindowsLifecycleMessages"/> for mapping.
/// </summary>
/// <remarks>
/// Two Windows paths feed the same mapper:
///
/// * A dedicated hidden top-level window. <c>WM_QUERYENDSESSION</c> and
///   <c>WM_ENDSESSION</c> are only delivered to top-level windows, so a message-only
///   (<c>HWND_MESSAGE</c>) window would never see a shutdown at all. It is separate
///   from the WinUI window because this app lives in the tray: the main window is
///   routinely closed, and a hook on a window that no longer exists is a hook that
///   silently stops working.
/// * <c>SystemEvents</c>, which supplies power and session-switch notifications.
///   Session-ending notifications deliberately stay on the raw window path because
///   the managed event discards <c>ENDSESSION_CLOSEAPP</c> and would misreport an
///   installer-requested app close as a machine shutdown.
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
    private const string WindowClassName = "WindowsCompanionLifecycleWindow";
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_DESTROY = 0x0002;

    // Long enough for a window to appear on a loaded machine, short enough that a
    // wedged pump never holds up sign-out.
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();

    // The agreed protocol with the pump thread. It has to cope with a Stop that
    // arrives before the window exists, which is otherwise a lost shutdown: the
    // stopper finds no window to close and the pump loops on forever.
    private readonly MessagePumpLifetime _lifetime = new();

    // The delegate is handed to unmanaged code, so it must outlive the P/Invoke that
    // registered it; a local would be collected while Windows still holds the pointer.
    private readonly WndProc _wndProc;

    private Thread? _pump;
    private nint _hwnd;

    public WindowsLifecycleSignalSource() => _wndProc = OnWindowMessage;

    public event Action<LifecycleSignal>? SignalObserved;

    public void Start()
    {
        lock (_gate)
        {
            if (!_lifetime.TryBeginStart()) return;

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;

            _hwnd = 0;
            _pump = new Thread(PumpMessages)
            {
                Name = "Windows Companion lifecycle",
                IsBackground = true
            };
            _pump.SetApartmentState(ApartmentState.STA);
            _pump.Start();
        }

        _lifetime.WaitUntilReady(ReadyTimeout);
    }

    public void Stop()
    {
        Thread? pump;

        lock (_gate)
        {
            // Recorded even if the pump has not created its window yet, so an early
            // stop is honoured rather than skipped.
            if (!_lifetime.RequestStop()) return;

            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;

            pump = _pump;
            _pump = null;
        }

        // Wait for the pump to publish its window - or to report that it never will,
        // having seen the request above - before deciding how to end it.
        _lifetime.WaitUntilReady(ReadyTimeout);

        nint hwnd;
        lock (_gate) hwnd = _hwnd;

        // Destroying the window has to happen on the thread that created it, so ask
        // its own pump to do it and let the loop end naturally.
        if (hwnd != 0) PostMessage(hwnd, WM_CLOSE, 0, 0);

        var ended = pump is null || pump.Join(ReadyTimeout);

        // The pump normally reports this itself; do it here too so a thread that
        // failed to start cannot leave the lifetime permanently claimed.
        if (ended) _lifetime.MarkStopped();
    }

    public void Dispose()
    {
        Stop();
        _lifetime.Dispose();
    }

    private void PumpMessages()
    {
        try
        {
            RunPump();
        }
        finally
        {
            lock (_gate) _hwnd = 0;

            // Readiness is announced again here in case we left before creating the
            // window, so a Stop waiting on it is never stranded.
            _lifetime.MarkStopped();
        }
    }

    private void RunPump()
    {
        // Stopped before we got going: nothing to create, and nothing to tear down.
        if (_lifetime.StopRequested) return;

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
            0, WindowClassName, "Windows Companion lifecycle", 0, 0, 0, 0, 0, 0, 0, instance, 0);

        lock (_gate) _hwnd = hwnd;

        // Published before readiness is announced, so a stopper that wakes on it is
        // guaranteed to see the handle.
        _lifetime.MarkReady();
        if (hwnd == 0) return;

        // A stop that arrived while the window was being created would have missed
        // its chance to post to it, so honour it here instead.
        if (_lifetime.StopRequested)
        {
            DestroyWindow(hwnd);
            return;
        }

        while (GetMessage(out var message, 0, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);
}
