using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using HaCompanion.Core.Models;
using HaCompanion.Core.Sensors;

namespace HaCompanion_App.Services;

public sealed class FrontmostAppSensorSource : ISensorSource
{
    public const string FrontmostAppId = "frontmost_app";

    private const uint EventSystemForeground = 0x0003;
    private const uint WineventOutOfContext = 0x0000;

    private readonly SensorPreferences _preferences;
    private readonly DebouncedValue<FrontmostAppSnapshot> _value = new();
    private readonly WinEventDelegate _callback;
    private readonly ManualResetEventSlim _hookReady = new(false);
    private readonly object _lifecycleGate = new();
    private Thread? _hookThread;
    private CancellationTokenSource? _debounceCancellation;
    private uint _hookThreadId;

    public FrontmostAppSensorSource(SensorPreferences preferences)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _callback = OnForegroundChanged;
    }

    public IReadOnlyList<SensorDefinition> Definitions { get; } =
    [
        new(
            FrontmostAppId,
            "Frontmost App",
            "The active application. Full window titles may reveal documents, messages and websites.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false)
    ];

    public IReadOnlyList<Sensor> Read(
        IReadOnlySet<string> enabled, SensorReadContext context)
    {
        if (!enabled.Contains(FrontmostAppId)) return [];

        var snapshot = _value.TryGetCurrent(out var current)
            ? current
            : Capture();
        return
        [
            new()
            {
                UniqueId = FrontmostAppId,
                Type = "sensor",
                Name = "Frontmost App",
                State = FrontmostAppState.Select(snapshot, _preferences.FrontmostAppMode),
                Icon = "mdi:application"
            }
        ];
    }

    public void Start(Action onChanged)
    {
        lock (_lifecycleGate)
        {
            if (_hookThread is not null) return;
            _hookReady.Reset();
            _hookThread = new Thread(HookThreadMain)
            {
                IsBackground = true,
                Name = "FrontmostAppHook"
            };
            _hookThread.Start();
        }

        _hookReady.Wait(TimeSpan.FromSeconds(5));
    }

    public void Stop()
    {
        _value.InvalidatePending();
        CancellationTokenSource? debounce;
        Thread? thread;
        uint threadId;
        lock (_lifecycleGate)
        {
            debounce = _debounceCancellation;
            debounce?.Cancel();
            _debounceCancellation = null;
            thread = _hookThread;
            threadId = _hookThreadId;
        }

        if (thread is null) return;
        if (threadId != 0) PostThreadMessage(threadId, WmQuit, 0, 0);
        thread.Join(TimeSpan.FromSeconds(5));

        lock (_lifecycleGate)
        {
            if (ReferenceEquals(_hookThread, thread))
                _hookThread = null;
        }
    }

    private void OnForegroundChanged(
        nint hook,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        var version = _value.Stage(Capture(window));
        var cancellation = new CancellationTokenSource();
        CancellationTokenSource? previous;
        lock (_lifecycleGate)
        {
            previous = _debounceCancellation;
            previous?.Cancel();
            _debounceCancellation = cancellation;
        }
        _ = CommitAfterSettleAsync(version, cancellation);
        // Deliberately no onChanged callback: the value rides the next normal batch.
    }

    private async Task CommitAfterSettleAsync(
        long version,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4), cancellation.Token).ConfigureAwait(false);
            _value.TryCommit(version);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_lifecycleGate)
            {
                if (ReferenceEquals(_debounceCancellation, cancellation))
                    _debounceCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private void HookThreadMain()
    {
        _hookThreadId = GetCurrentThreadId();
        PeekMessage(out _, 0, 0, 0, PeekMessageNoRemove);

        var hook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            0,
            _callback,
            0,
            0,
            WineventOutOfContext);

        _value.SetInitial(Capture());
        _hookReady.Set();

        try
        {
            if (hook == 0) return;
            while (GetMessage(out var message, 0, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        finally
        {
            if (hook != 0) UnhookWinEvent(hook);
            lock (_lifecycleGate)
            {
                _hookThreadId = 0;
                if (ReferenceEquals(_hookThread, Thread.CurrentThread))
                    _hookThread = null;
            }
        }
    }

    private static FrontmostAppSnapshot Capture(nint window = 0)
    {
        if (window == 0) window = GetForegroundWindow();
        if (window == 0) return default;

        GetWindowThreadProcessId(window, out var processId);
        var application = GetApplicationName(processId);
        var title = GetTitle(window);
        return new FrontmostAppSnapshot(application, title);
    }

    private static string? GetApplicationName(uint processId)
    {
        if (processId == 0) return null;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName + ".exe";
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string? GetTitle(nint window)
    {
        var length = GetWindowTextLength(window);
        if (length <= 0) return null;

        var title = new StringBuilder(length + 1);
        return GetWindowText(window, title, title.Capacity) > 0
            ? title.ToString()
            : null;
    }

    private delegate void WinEventDelegate(
        nint hook,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    private const uint WmQuit = 0x0012;
    private const uint PeekMessageNoRemove = 0x0000;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Value;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public Point Location;
        public uint Private;
    }

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint module,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint window);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(
        uint threadId, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(
        out Message message, nint window, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(
        out Message message, nint window, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref Message message);
}
