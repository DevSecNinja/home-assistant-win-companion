using System.Runtime.InteropServices;
using HaCompanion.Core.Models;
using HaCompanion.Core.Sensors;
using Microsoft.Win32;

namespace HaCompanion_App.Services;

/// <summary>
/// Reports how many displays are active and what modes they are running, using
/// the supported Windows monitor and Connecting-and-Configuring-Displays (CCD)
/// APIs. No external process, no WMI, no EDID parsing.
/// </summary>
/// <remarks>
/// Both sensors are served by a single enumeration, so enabling the second one
/// costs nothing extra. Nothing that identifies a specific monitor - EDID serial,
/// friendly name, device path or monitor id - is collected: only mode
/// information and whether the panel is built in.
///
/// Topology changes (dock, undock, resolution, scaling, refresh rate) raise
/// <see cref="SystemEvents.DisplaySettingsChanged"/>, so there is no polling at
/// all; the hook exists only while one of these sensors is enabled.
/// </remarks>
public sealed class DisplaySensorSource : ISensorSource
{
    public const string DisplayCountId = "displays_count";
    public const string DisplayResolutionId = "display_resolution";

    private readonly ChangeGate<string> _summary = new(string.Empty);
    private Action? _onChanged;
    private bool _observing;

    public IReadOnlyList<SensorDefinition> Definitions { get; } =
    [
        new(
            DisplayCountId,
            "Displays",
            "How many displays are currently active on this PC.",
            SensorPrivacy.Benign,
            EnabledByDefault: true),
        new(
            DisplayResolutionId,
            "Display Resolution",
            "The resolutions, refresh rates and scaling of the active displays. "
            + "Reveals more about this PC's hardware, so it is off by default.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false)
    ];

    public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context)
    {
        if (!enabled.Contains(DisplayCountId) && !enabled.Contains(DisplayResolutionId))
            return [];

        var displays = Enumerate();
        var summary = DisplaySummary.Describe(displays);
        _summary.Seed(summary);

        var count = DisplaySummary.Count(displays);
        var readings = new List<Sensor>();

        if (enabled.Contains(DisplayCountId))
        {
            readings.Add(new Sensor
            {
                UniqueId = DisplayCountId,
                Type = "sensor",
                Name = "Displays",
                State = count,
                StateClass = "measurement",
                EntityCategory = "diagnostic",
                Icon = DisplaySummary.IconFor(count)
            });
        }

        if (enabled.Contains(DisplayResolutionId))
        {
            readings.Add(new Sensor
            {
                UniqueId = DisplayResolutionId,
                Type = "sensor",
                Name = "Display Resolution",
                State = summary,
                EntityCategory = "diagnostic",
                Icon = DisplaySummary.IconFor(count),
                Attributes = DisplaySummary.BuildAttributes(displays)
            });
        }

        return readings;
    }

    public void Start(Action onChanged)
    {
        _onChanged = onChanged;
        if (_observing) return;

        _summary.Seed(DisplaySummary.Describe(Enumerate()));
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _observing = true;
    }

    public void Stop()
    {
        if (!_observing) return;

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _observing = false;
    }

    /// <summary>
    /// Windows raises this several times while a dock settles, so the reading is
    /// compared before a push is requested.
    /// </summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (_summary.TryUpdate(DisplaySummary.Describe(Enumerate())))
            _onChanged?.Invoke();
    }

    /// <summary>
    /// One pass over the active monitors: mode and DPI from the monitor APIs,
    /// built-in/external classification from the CCD path table.
    /// </summary>
    private static IReadOnlyList<DisplayInfo> Enumerate()
    {
        var displays = new List<DisplayInfo>();

        try
        {
            var connections = ReadConnections();

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
            {
                var info = new MONITORINFOEXW { cbSize = Marshal.SizeOf<MONITORINFOEXW>() };
                if (!GetMonitorInfoW(monitor, ref info)) return true;

                var device = info.szDevice ?? string.Empty;
                var scale = ReadScalePercent(monitor);
                var width = info.rcMonitor.Width;
                var height = info.rcMonitor.Height;
                var refresh = 0;

                var mode = new DEVMODEW { dmSize = (ushort)Marshal.SizeOf<DEVMODEW>() };
                if (device.Length > 0 && EnumDisplaySettingsW(device, ENUM_CURRENT_SETTINGS, ref mode))
                {
                    // The monitor rectangle is in scaled coordinates; DEVMODE reports
                    // the physical pixels the user recognises as "the resolution".
                    if (mode.dmPelsWidth > 0) width = (int)mode.dmPelsWidth;
                    if (mode.dmPelsHeight > 0) height = (int)mode.dmPelsHeight;

                    // 0 and 1 are the documented "hardware default" placeholders.
                    if (mode.dmDisplayFrequency > 1) refresh = (int)mode.dmDisplayFrequency;
                }

                displays.Add(new DisplayInfo(
                    width,
                    height,
                    refresh,
                    scale,
                    connections.TryGetValue(device, out var connection)
                        ? connection
                        : DisplayConnection.Unknown,
                    (info.dwFlags & MONITORINFOF_PRIMARY) != 0));

                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Nothing to report rather than a crash on a stripped-down Windows SKU.
            return [];
        }

        return displays;
    }

    private static int ReadScalePercent(IntPtr monitor)
    {
        try
        {
            return GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0
                ? (int)Math.Round(dpiX * 100d / 96d)
                : 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Maps each GDI display device ("\\.\DISPLAY1") to whether its output is an
    /// internal panel, using the CCD path table. Failures degrade to
    /// <see cref="DisplayConnection.Unknown"/> rather than guessing.
    /// </summary>
    private static Dictionary<string, DisplayConnection> ReadConnections()
    {
        var connections = new Dictionary<string, DisplayConnection>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount) != 0)
                return connections;

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

            if (QueryDisplayConfig(
                    QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
            {
                return connections;
            }

            for (var i = 0; i < pathCount; i++)
            {
                var path = paths[i];
                var request = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        size = Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                        adapterId = path.sourceInfo.adapterId,
                        id = path.sourceInfo.id
                    }
                };

                if (DisplayConfigGetDeviceInfo(ref request) != 0) continue;
                if (string.IsNullOrEmpty(request.viewGdiDeviceName)) continue;

                connections[request.viewGdiDeviceName] = IsInternal(path.targetInfo.outputTechnology)
                    ? DisplayConnection.Internal
                    : DisplayConnection.External;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return connections;
        }

        return connections;
    }

    private static bool IsInternal(uint outputTechnology) => outputTechnology
        is DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL
        or DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS
        or DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED
        or DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED;

    private const uint MONITORINFOF_PRIMARY = 1;
    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int MDT_EFFECTIVE_DPI = 0;
    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    private const uint DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS = 6;
    private const uint DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED = 11;
    private const uint DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED = 13;
    private const uint DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL = 0x80000000;

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr clip, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;

        public readonly int Width => right - left;
        public readonly int Height => bottom - top;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEXW
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODEW
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;

        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;

        // The display half of the DEVMODE union: position, orientation, fixed output.
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;

        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;

        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public uint refreshRateNumerator;
        public uint refreshRateDenominator;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    /// <summary>
    /// Only the header is read; the 48-byte mode union is reserved by size so the
    /// array marshals with the exact layout <c>QueryDisplayConfig</c> expects.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        [FieldOffset(0)] public uint infoType;
        [FieldOffset(4)] public uint id;
        [FieldOffset(8)] public LUID adapterId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public int size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string viewGdiDeviceName;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFOEXW info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettingsW(string deviceName, int modeNum, ref DEVMODEW mode);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(
        uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint pathCount,
        [Out] DISPLAYCONFIG_PATH_INFO[] paths,
        ref uint modeCount,
        [Out] DISPLAYCONFIG_MODE_INFO[] modes,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME request);
}
