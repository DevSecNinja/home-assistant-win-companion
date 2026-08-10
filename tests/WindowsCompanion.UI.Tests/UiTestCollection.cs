using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core;

namespace WindowsCompanion.UI.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UiTestCollection
{
    public const string Name = "Windows Companion UI";
}

internal static class UiCapabilities
{
    internal static string? InteractiveUnsupportedReason()
    {
#if !DEBUG
        return "Interactive UI capability unavailable: UI automation requires a Debug app build.";
#else
        if (!Environment.UserInteractive)
            return "Interactive UI capability unavailable: the process is not in an interactive session.";

        var desktop = OpenInputDesktop(0, false, DesktopReadObjects | DesktopSwitchDesktop);
        if (desktop == 0)
            return "Interactive UI capability unavailable: no unlocked input desktop is accessible.";
        CloseDesktop(desktop);
        return null;
#endif
    }

    internal static string? TrayUnsupportedReason()
    {
        var interactive = InteractiveUnsupportedReason();
        if (interactive is not null) return interactive;
        if (!string.Equals(
                Environment.GetEnvironmentVariable("WINDOWS_COMPANION_UI_TRAY"),
                "1",
                StringComparison.Ordinal))
        {
            return "Tray capability unavailable: WINDOWS_COMPANION_UI_TRAY=1 is not set.";
        }
        if (FindWindow("Shell_TrayWnd", null) == 0)
            return "Tray capability unavailable: the Windows taskbar shell is not running in this desktop.";
        return null;
    }

    internal static string? NotificationUnsupportedReason()
    {
        var interactive = InteractiveUnsupportedReason();
        if (interactive is not null) return interactive;
        if (!string.Equals(
                Environment.GetEnvironmentVariable("WINDOWS_COMPANION_UI_NOTIFICATIONS"),
                "1",
                StringComparison.Ordinal))
        {
            return "Notification capability unavailable: WINDOWS_COMPANION_UI_NOTIFICATIONS=1 is not set.";
        }
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            return "Notification capability unavailable: Windows 10 build 17763 or later is required.";
        return null;
    }

    internal static bool IsWindowVisible(Window window) =>
        IsWindowVisible((nint)window.Properties.NativeWindowHandle.Value);

    internal static IReadOnlyList<AutomationElement> TrayAutomationRoots(AutomationBase automation)
    {
        var handles = new[]
        {
            FindWindow("Shell_TrayWnd", null),
            FindWindow("NotifyIconOverflowWindow", null)
        };
        return handles
            .Where(handle => handle != 0)
            .Select(handle => automation.FromHandle(handle))
            .ToArray();
    }

    private const uint DesktopReadObjects = 0x0001;
    private const uint DesktopSwitchDesktop = 0x0100;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint OpenInputDesktop(
        uint flags,
        [MarshalAs(UnmanagedType.Bool)] bool inherit,
        uint desiredAccess);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(nint desktop);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
}

internal sealed class UiFactAttribute : FactAttribute
{
    public UiFactAttribute() => Skip = UiCapabilities.InteractiveUnsupportedReason();
}

internal sealed class UiTheoryAttribute : TheoryAttribute
{
    public UiTheoryAttribute() => Skip = UiCapabilities.InteractiveUnsupportedReason();
}

internal sealed class UiNotificationFactAttribute : FactAttribute
{
    public UiNotificationFactAttribute() => Skip = UiCapabilities.NotificationUnsupportedReason();
}

internal sealed class UiTrayFactAttribute : FactAttribute
{
    public UiTrayFactAttribute() => Skip = UiCapabilities.TrayUnsupportedReason();
}
