using System.Runtime.InteropServices;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;
using Microsoft.Win32;

namespace WindowsCompanion_App.Services;

/// <summary>
/// Reports active display count, modes and opt-in monitor identity using the
/// supported Windows monitor and Connecting-and-Configuring-Displays (CCD) APIs.
/// No external process, WMI or raw EDID parsing is used.
/// </summary>
/// <remarks>
/// All enabled display sensors are served by one capture. Monitor device paths
/// are used only as in-memory deduplication keys and are never sent or logged.
///
/// Topology changes (dock, undock, resolution, scaling, refresh rate) raise
/// <see cref="SystemEvents.DisplaySettingsChanged"/>, so there is no polling at
/// all; the hook exists only while one of these sensors is enabled.
/// </remarks>
public sealed class DisplaySensorSource : ISensorSource
{
    public const string DisplayCountId = DisplayCapturePolicy.DisplayCountId;
    public const string DisplayResolutionId = DisplayCapturePolicy.DisplayResolutionId;
    public const string DisplayIdentityId = DisplayCapturePolicy.DisplayIdentityId;

    private readonly SensorPreferences _preferences;
    private readonly DisplayObservationGate _observations;
    private readonly ChangeGate<string> _identity = new(string.Empty);
    private Action? _onChanged;
    private bool _observing;

    public DisplaySensorSource(SensorPreferences preferences)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _observations = new DisplayObservationGate(
            CountDisplays,
            () => Enumerate(includeModeDetails: true, includeIdentity: false).Displays);
    }

    public IReadOnlyList<SensorDefinition> Definitions { get; } =
    [
        new(
            DisplayCountId,
            "Displays",
            "How many displays are currently active on this PC.",
            SensorPrivacy.Benign,
            EnabledByDefault: true,
            ResourceUsage: "Low. Does not check repeatedly. Counts active displays and sends an "
                           + "extra update only when Windows reports a display change.",
            AutomationIdea: "When a second display connects, activate the office work scene."),
        new(
            DisplayResolutionId,
            "Display Resolution",
            "The resolutions, refresh rates and scaling of the active displays. "
            + "Reveals more about this PC's hardware, so it is off by default.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false,
            ResourceUsage: "Low. Shares the display check above. It does not use the internet."),
        new(
            DisplayIdentityId,
            "Display Identity",
            "The brand and model/type of active physical displays. The count always includes "
            + "every display, while attributes list at most the first 8 in stable order. "
            + "Reveals hardware identity, so it is off by default.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false,
            ResourceUsage: "Low. Shares the display-change check above and reads display names "
                           + "only while enabled. It does not use the internet.",
            AutomationIdea: "When a known display is attached, activate the matching workspace.")
        {
            SearchAliases = ["Monitor", "Monitors"]
        }
    ];

    public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context)
    {
        var wantsCount = enabled.Contains(DisplayCountId);
        var wantsResolution = enabled.Contains(DisplayResolutionId);
        var wantsIdentity = enabled.Contains(DisplayIdentityId);

        if (!wantsCount && !wantsResolution && !wantsIdentity)
            return [];

        var readings = new List<Sensor>();

        if (wantsIdentity)
        {
            var snapshot = Enumerate(wantsResolution, includeIdentity: true);
            var count = snapshot.DisplayCount;
            _identity.Seed(MonitorIdentitySummary.Signature(snapshot.Monitors));

            if (wantsResolution)
                _observations.SeedDetails(snapshot.Displays);
            else if (wantsCount)
                _observations.SeedCount(count);

            if (wantsCount)
                readings.Add(BuildCountSensor(count));

            if (wantsResolution)
                readings.Add(BuildResolutionSensor(snapshot.Displays));

            var monitorCount = snapshot.Monitors.Status == MonitorCaptureStatus.Available
                ? MonitorIdentitySummary.Order(snapshot.Monitors.Monitors).Count
                : 0;
            readings.Add(new Sensor
            {
                UniqueId = DisplayIdentityId,
                Type = "sensor",
                Name = "Display Identity",
                State = MonitorIdentitySummary.Describe(snapshot.Monitors),
                EntityCategory = "diagnostic",
                Icon = DisplaySummary.IconFor(monitorCount),
                Attributes = MonitorIdentitySummary.BuildAttributes(snapshot.Monitors)
            });

            return readings;
        }

        // The sensitive resolution details are only gathered once that sensor is
        // itself enabled/permitted, so a count-only caller (including a preview
        // where the resolution sensor is off) never collects them at all.
        if (wantsResolution)
        {
            var displays = _observations.CaptureDetails();
            var count = DisplaySummary.Count(displays);

            if (wantsCount)
                readings.Add(BuildCountSensor(count));

            readings.Add(BuildResolutionSensor(displays));
        }
        else if (wantsCount)
        {
            var count = _observations.CaptureCount();
            readings.Add(BuildCountSensor(count));
        }

        return readings;
    }

    private static Sensor BuildCountSensor(int count) => new()
    {
        UniqueId = DisplayCountId,
        Type = "sensor",
        Name = "Displays",
        State = count,
        StateClass = "measurement",
        EntityCategory = "diagnostic",
        Icon = DisplaySummary.IconFor(count)
    };

    private static Sensor BuildResolutionSensor(IReadOnlyList<DisplayInfo> displays)
    {
        var count = DisplaySummary.Count(displays);
        return new Sensor
        {
            UniqueId = DisplayResolutionId,
            Type = "sensor",
            Name = "Display Resolution",
            State = DisplaySummary.Describe(displays),
            EntityCategory = "diagnostic",
            Icon = DisplaySummary.IconFor(count),
            Attributes = DisplaySummary.BuildAttributes(displays)
        };
    }

    /// <summary>
    /// Counts active displays without touching mode, scaling or connection
    /// details, so the benign <see cref="DisplayCountId"/> sensor never has to
    /// gather anything the sensitive <see cref="DisplayResolutionId"/> sensor
    /// reports.
    /// </summary>
    private static int CountDisplays()
    {
        var count = 0;

        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (_, _, _, _) =>
            {
                count++;
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return 0;
        }

        return count;
    }

    public void Start(Action onChanged)
    {
        _onChanged = onChanged;
        if (_observing) return;

        var enabled = EnabledIds();
        var scope = DisplayCapturePolicy.For(enabled);
        if (scope == DisplayCaptureScope.Identity)
            SeedIdentityObservation(
                enabled,
                Enumerate(enabled.Contains(DisplayResolutionId), includeIdentity: true));
        else
            _observations.Seed(scope);

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
        var enabled = EnabledIds();
        var scope = DisplayCapturePolicy.For(enabled);
        var changed = scope == DisplayCaptureScope.Identity
            ? TryUpdateIdentityObservation(
                enabled,
                Enumerate(enabled.Contains(DisplayResolutionId), includeIdentity: true))
            : _observations.TryUpdate(scope);

        if (changed)
            _onChanged?.Invoke();
    }

    private void SeedIdentityObservation(
        IReadOnlySet<string> enabled,
        DisplayCaptureSnapshot snapshot)
    {
        _identity.Seed(MonitorIdentitySummary.Signature(snapshot.Monitors));
        if (enabled.Contains(DisplayResolutionId))
            _observations.SeedDetails(snapshot.Displays);
        else if (enabled.Contains(DisplayCountId))
            _observations.SeedCount(snapshot.DisplayCount);
    }

    private bool TryUpdateIdentityObservation(
        IReadOnlySet<string> enabled,
        DisplayCaptureSnapshot snapshot)
    {
        var changed = _identity.TryUpdate(MonitorIdentitySummary.Signature(snapshot.Monitors));
        if (enabled.Contains(DisplayResolutionId))
            changed |= _observations.TryUpdateDetails(snapshot.Displays);
        else if (enabled.Contains(DisplayCountId))
            changed |= _observations.TryUpdateCount(snapshot.DisplayCount);
        return changed;
    }

    private IReadOnlySet<string> EnabledIds() =>
        Definitions
            .Where(_preferences.IsEnabled)
            .Select(definition => definition.UniqueId)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Captures logical display modes once and enriches them from the same active
    /// CCD path table used for opt-in monitor identity.
    /// </summary>
    private static DisplayCaptureSnapshot Enumerate(
        bool includeModeDetails,
        bool includeIdentity)
    {
        try
        {
            var logicalDisplays = includeModeDetails ? ReadLogicalDisplays() : [];
            var primaryDevices = includeModeDetails
                ? logicalDisplays
                    .Where(display => display.IsPrimary)
                    .Select(display => display.DeviceName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : ReadPrimaryDisplayDevices();
            var topology = ReadTopology(includeIdentity, primaryDevices);

            var displays = logicalDisplays
                .Select(display => new DisplayInfo(
                    display.Width,
                    display.Height,
                    display.RefreshRateHz,
                    display.ScalePercent,
                    topology.Connections.TryGetValue(display.DeviceName, out var connection)
                        ? connection
                        : DisplayConnection.Unknown,
                    display.IsPrimary))
                .ToArray();

            var monitors = includeIdentity
                ? topology.IdentityAvailable
                    ? MonitorCaptureResult.Available(topology.Monitors)
                    : MonitorCaptureResult.Unavailable
                : MonitorCaptureResult.Available([]);
            var displayCount = includeModeDetails
                ? DisplaySummary.Count(displays)
                : CountDisplays();

            return new DisplayCaptureSnapshot(displays, displayCount, monitors);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return new DisplayCaptureSnapshot(
                [],
                0,
                includeIdentity
                    ? MonitorCaptureResult.Unavailable
                    : MonitorCaptureResult.Available([]));
        }
    }

    private static IReadOnlyList<LogicalDisplay> ReadLogicalDisplays()
    {
        var displays = new List<LogicalDisplay>();

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

            displays.Add(new LogicalDisplay(
                device,
                width,
                height,
                refresh,
                scale,
                (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
            return true;
        }, IntPtr.Zero);

        return displays;
    }

    private static IReadOnlySet<string> ReadPrimaryDisplayDevices()
    {
        var primary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (uint index = 0; ; index++)
        {
            var device = new DISPLAY_DEVICEW { cb = Marshal.SizeOf<DISPLAY_DEVICEW>() };
            if (!EnumDisplayDevicesW(null, index, ref device, 0)) break;
            if ((device.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;
            if ((device.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) == 0) continue;
            if (!string.IsNullOrEmpty(device.DeviceName))
                primary.Add(device.DeviceName);
        }

        return primary;
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

    private static TopologyCapture ReadTopology(
        bool includeIdentity,
        IReadOnlySet<string> primaryDevices)
    {
        var connections = new Dictionary<string, DisplayConnection>(StringComparer.OrdinalIgnoreCase);
        var monitors = new List<MonitorIdentity>();

        if (!TryReadActivePaths(out var paths))
            return new TopologyCapture(connections, monitors, IdentityAvailable: !includeIdentity);

        var identityAvailable = true;
        foreach (var path in paths)
        {
            var sourceRequest = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
            {
                header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                    size = Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                    adapterId = path.sourceInfo.adapterId,
                    id = path.sourceInfo.id
                }
            };

            var sourceResult = DisplayConfigGetDeviceInfo(ref sourceRequest);
            var sourceName = sourceResult == ERROR_SUCCESS
                ? sourceRequest.viewGdiDeviceName ?? string.Empty
                : string.Empty;
            var connection = IsInternal(path.targetInfo.outputTechnology)
                ? DisplayConnection.Internal
                : DisplayConnection.External;

            if (sourceName.Length > 0)
                connections[sourceName] = MergeConnection(
                    connections.GetValueOrDefault(sourceName, DisplayConnection.Unknown),
                    connection);

            if (!includeIdentity
                || path.targetInfo.targetAvailable == 0
                || !IsPhysical(path.targetInfo.outputTechnology))
            {
                continue;
            }

            if (sourceName.Length == 0)
                identityAvailable = false;

            var targetRequest = new DISPLAYCONFIG_TARGET_DEVICE_NAME
            {
                header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                    size = Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                    adapterId = path.targetInfo.adapterId,
                    id = path.targetInfo.id
                }
            };

            if (DisplayConfigGetDeviceInfo(ref targetRequest) != ERROR_SUCCESS)
            {
                identityAvailable = false;
                continue;
            }

            if ((targetRequest.flags & DISPLAYCONFIG_TARGET_FRIENDLY_NAME_FORCED) != 0)
                continue;

            var edidIdsValid = (targetRequest.flags & DISPLAYCONFIG_TARGET_EDID_IDS_VALID) != 0;
            var trustedName = (targetRequest.flags & DISPLAYCONFIG_TARGET_FRIENDLY_NAME_FROM_EDID) != 0
                              || edidIdsValid;
            var internalKey = string.IsNullOrWhiteSpace(targetRequest.monitorDevicePath)
                ? BuildTargetKey(path.targetInfo.adapterId, path.targetInfo.id)
                : targetRequest.monitorDevicePath;
            monitors.Add(MonitorIdentity.Create(
                internalKey,
                targetRequest.monitorFriendlyDeviceName,
                trustedName,
                targetRequest.edidManufactureId,
                targetRequest.edidProductCodeId,
                edidIdsValid,
                connection,
                primaryDevices.Contains(sourceName)));
        }

        return new TopologyCapture(connections, monitors, identityAvailable);
    }

    private static bool TryReadActivePaths(out IReadOnlyList<DISPLAYCONFIG_PATH_INFO> activePaths)
    {
        for (var attempt = 0; attempt < DisplayConfigBufferAttempts; attempt++)
        {
            if (GetDisplayConfigBufferSizes(
                    QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount) != ERROR_SUCCESS)
            {
                activePaths = [];
                return false;
            }

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            var result = QueryDisplayConfig(
                QDC_ONLY_ACTIVE_PATHS,
                ref pathCount,
                paths,
                ref modeCount,
                modes,
                IntPtr.Zero);
            if (result == ERROR_SUCCESS)
            {
                activePaths = paths.Take((int)pathCount).ToArray();
                return true;
            }

            if (result != ERROR_INSUFFICIENT_BUFFER)
            {
                activePaths = [];
                return false;
            }
        }

        activePaths = [];
        return false;
    }

    private static bool IsInternal(uint outputTechnology) => outputTechnology
        is DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL
        or DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS
        or DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED
        or DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED;

    private static bool IsPhysical(uint outputTechnology) => outputTechnology
        is not DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INDIRECT_WIRED
        and not DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INDIRECT_VIRTUAL;

    private static DisplayConnection MergeConnection(
        DisplayConnection current,
        DisplayConnection candidate) =>
        current == DisplayConnection.Internal || candidate == DisplayConnection.Internal
            ? DisplayConnection.Internal
            : candidate;

    private static string BuildTargetKey(LUID adapterId, uint targetId) =>
        $"{adapterId.HighPart:X8}:{adapterId.LowPart:X8}:{targetId:X8}";

    private sealed record LogicalDisplay(
        string DeviceName,
        int Width,
        int Height,
        int RefreshRateHz,
        int ScalePercent,
        bool IsPrimary);

    private sealed record DisplayCaptureSnapshot(
        IReadOnlyList<DisplayInfo> Displays,
        int DisplayCount,
        MonitorCaptureResult Monitors);

    private sealed record TopologyCapture(
        IReadOnlyDictionary<string, DisplayConnection> Connections,
        IReadOnlyList<MonitorIdentity> Monitors,
        bool IdentityAvailable);

    private const int DisplayConfigBufferAttempts = 3;
    private const int ERROR_SUCCESS = 0;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const uint MONITORINFOF_PRIMARY = 1;
    private const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 1;
    private const uint DISPLAY_DEVICE_PRIMARY_DEVICE = 4;
    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int MDT_EFFECTIVE_DPI = 0;
    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;
    private const uint DISPLAYCONFIG_TARGET_FRIENDLY_NAME_FROM_EDID = 1;
    private const uint DISPLAYCONFIG_TARGET_FRIENDLY_NAME_FORCED = 2;
    private const uint DISPLAYCONFIG_TARGET_EDID_IDS_VALID = 4;
    private const uint DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS = 6;
    private const uint DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED = 11;
    private const uint DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED = 13;
    private const uint DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INDIRECT_WIRED = 16;
    private const uint DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INDIRECT_VIRTUAL = 17;
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
    private struct DISPLAY_DEVICEW
    {
        public int cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevicesW(
        string? device,
        uint deviceNumber,
        ref DISPLAY_DEVICEW displayDevice,
        uint flags);

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

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME request);
}
