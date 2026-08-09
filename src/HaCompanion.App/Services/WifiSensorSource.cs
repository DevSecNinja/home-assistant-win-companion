using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using HaCompanion.Core.Models;
using HaCompanion.Core.Sensors;

namespace HaCompanion_App.Services;

public sealed class WifiSensorSource : ISensorSource
{
    public const string SsidId = "connectivity_ssid";
    public const string BssidId = "connectivity_bssid";

    private readonly SensorPreferences _preferences;
    private Action? _onChanged;
    private bool _observing;

    public WifiSensorSource(SensorPreferences preferences)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
    }

    public IReadOnlyList<SensorDefinition> Definitions { get; } =
    [
        new(
            SsidId,
            "Wi-Fi SSID",
            "The connected Wi-Fi network name. Windows treats this as location data.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false,
            ResourceUsage: "Usually low. Sends an extra update when Windows reports a network "
                           + "change. Windows can report several changes close together."),
        new(
            BssidId,
            "Wi-Fi BSSID",
            "The connected access point identifier. Windows treats this as precise location data.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false,
            ResourceUsage: "Usually low. Shares the Wi-Fi check with SSID and sends an extra update "
                           + "for each Windows network-change notice.")
    ];

    public IReadOnlyList<Sensor> Read(
        IReadOnlySet<string> enabled, SensorReadContext context)
    {
        var info = ReadConnection();
        var sensors = new List<Sensor>();

        if (enabled.Contains(SsidId))
        {
            sensors.Add(new Sensor
            {
                UniqueId = SsidId,
                Type = "sensor",
                Name = "Wi-Fi SSID",
                State = info.SsidState,
                Icon = "mdi:wifi"
            });
        }

        if (enabled.Contains(BssidId))
        {
            sensors.Add(new Sensor
            {
                UniqueId = BssidId,
                Type = "sensor",
                Name = "Wi-Fi BSSID",
                State = info.BssidState,
                EntityCategory = "diagnostic",
                Icon = "mdi:access-point"
            });
        }

        return sensors;
    }

    public ValueTask<IReadOnlyList<Sensor>> PreviewAsync(
        IReadOnlySet<string> requested,
        CancellationToken cancellationToken = default)
    {
        if (!Definitions.Any(_preferences.IsEnabled))
        {
            return ValueTask.FromResult<IReadOnlyList<Sensor>>(
            [
                new() { UniqueId = SsidId, Name = "Wi-Fi SSID", State = "Enable to read Wi-Fi identifiers" },
                new() { UniqueId = BssidId, Name = "Wi-Fi BSSID", State = "Enable to read Wi-Fi identifiers" }
            ]);
        }

        return ValueTask.FromResult(Read(requested, new SensorReadContext("Preview")));
    }

    public void Start(Action onChanged)
    {
        _onChanged = onChanged;
        if (_observing) return;
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        _observing = true;
    }

    public void Stop()
    {
        if (!_observing) return;
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        _observing = false;
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => _onChanged?.Invoke();

    internal static WifiConnectionInfo ReadConnection()
    {
        var result = WlanOpenHandle(2, 0, out _, out var client);
        if (result != 0) return new(WifiConnectionStatus.Unavailable);

        try
        {
            result = WlanEnumInterfaces(client, 0, out var listPointer);
            if (result != 0) return new(WifiConnectionStatus.Unavailable);

            try
            {
                var count = Marshal.ReadInt32(listPointer);
                var offset = sizeof(int) * 2;
                var size = Marshal.SizeOf<WlanInterfaceInfo>();

                for (var index = 0; index < count; index++)
                {
                    var itemPointer = IntPtr.Add(listPointer, offset + index * size);
                    var item = Marshal.PtrToStructure<WlanInterfaceInfo>(itemPointer);
                    if (item.State != WlanInterfaceState.Connected) continue;

                    result = WlanQueryInterface(
                        client,
                        ref item.InterfaceGuid,
                        WlanIntfOpcode.CurrentConnection,
                        0,
                        out _,
                        out var dataPointer,
                        out _);

                    if (result == ErrorAccessDenied)
                        return new(WifiConnectionStatus.PermissionRequired);
                    if (result != 0)
                        continue;

                    try
                    {
                        var connection =
                            Marshal.PtrToStructure<WlanConnectionAttributes>(dataPointer);
                        var length = Math.Min(
                            (int)connection.Association.Dot11Ssid.Length,
                            connection.Association.Dot11Ssid.Ssid.Length);
                        var ssid = Encoding.UTF8.GetString(
                            connection.Association.Dot11Ssid.Ssid,
                            0,
                            length);
                        return new(
                            WifiConnectionStatus.Connected,
                            ssid,
                            connection.Association.Dot11Bssid);
                    }
                    finally
                    {
                        WlanFreeMemory(dataPointer);
                    }
                }

                return new(WifiConnectionStatus.NotConnected);
            }
            finally
            {
                WlanFreeMemory(listPointer);
            }
        }
        finally
        {
            WlanCloseHandle(client, 0);
        }
    }

    private const int ErrorAccessDenied = 5;

    private enum WlanInterfaceState
    {
        NotReady,
        Connected
    }

    private enum WlanIntfOpcode
    {
        CurrentConnection = 7
    }

    private enum Dot11BssType
    {
        Infrastructure = 1,
        Independent = 2,
        Any = 3
    }

    private enum Dot11PhyType
    {
        Unknown = 0
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanInterfaceInfo
    {
        public Guid InterfaceGuid;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Description;

        public WlanInterfaceState State;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanConnectionAttributes
    {
        public WlanInterfaceState State;
        public int ConnectionMode;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string ProfileName;

        public WlanAssociationAttributes Association;
        public WlanSecurityAttributes Security;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dot11Ssid
    {
        public uint Length;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Ssid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanAssociationAttributes
    {
        public Dot11Ssid Dot11Ssid;
        public Dot11BssType Dot11BssType;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] Dot11Bssid;

        public Dot11PhyType Dot11PhyType;
        public uint Dot11PhyIndex;
        public uint SignalQuality;
        public uint RxRate;
        public uint TxRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanSecurityAttributes
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool SecurityEnabled;

        [MarshalAs(UnmanagedType.Bool)]
        public bool OneXEnabled;

        public int AuthAlgorithm;
        public int CipherAlgorithm;
    }

    [DllImport("wlanapi.dll")]
    private static extern int WlanOpenHandle(
        uint clientVersion,
        nint reserved,
        out uint negotiatedVersion,
        out nint clientHandle);

    [DllImport("wlanapi.dll")]
    private static extern int WlanCloseHandle(nint clientHandle, nint reserved);

    [DllImport("wlanapi.dll")]
    private static extern int WlanEnumInterfaces(
        nint clientHandle,
        nint reserved,
        out nint interfaceList);

    [DllImport("wlanapi.dll")]
    private static extern int WlanQueryInterface(
        nint clientHandle,
        ref Guid interfaceGuid,
        WlanIntfOpcode opcode,
        nint reserved,
        out uint dataSize,
        out nint data,
        out int opcodeValueType);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(nint memory);
}
