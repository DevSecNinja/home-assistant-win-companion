using System.ComponentModel;
using System.Runtime.InteropServices;
using WindowsCompanion.Core.Lifecycle;

namespace WindowsCompanion_App.Services;

/// <summary>
/// Observes the Restart Manager end-session message on the WinUI window.
/// </summary>
public sealed class RestartManagerShutdownMonitor : IDisposable
{
    private const nuint SubclassId = 1;

    private readonly nint _windowHandle;
    private readonly Action _shutdownRequested;
    private readonly SubclassProc _subclassProc;
    private bool _disposed;

    public RestartManagerShutdownMonitor(nint windowHandle, Action shutdownRequested)
    {
        _windowHandle = windowHandle;
        _shutdownRequested = shutdownRequested;
        _subclassProc = OnWindowMessage;

        if (!SetWindowSubclass(_windowHandle, _subclassProc, SubclassId, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not observe Restart Manager shutdown.");
    }

    private nint OnWindowMessage(
        nint windowHandle,
        uint message,
        nuint wParam,
        nint lParam,
        nuint subclassId,
        nuint referenceData)
    {
        if (WindowsLifecycleMessages.IsRestartManagerShutdown(message, (nint)wParam, lParam))
            _shutdownRequested();

        return DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (!RemoveWindowSubclass(_windowHandle, _subclassProc, SubclassId))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not remove Restart Manager shutdown observer.");
    }

    private delegate nint SubclassProc(
        nint windowHandle,
        uint message,
        nuint wParam,
        nint lParam,
        nuint subclassId,
        nuint referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        nint windowHandle,
        SubclassProc subclassProc,
        nuint subclassId,
        nuint referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        nint windowHandle,
        SubclassProc subclassProc,
        nuint subclassId);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(
        nint windowHandle,
        uint message,
        nuint wParam,
        nint lParam);
}
