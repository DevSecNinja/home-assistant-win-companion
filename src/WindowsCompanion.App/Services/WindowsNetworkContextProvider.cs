using System.Net.NetworkInformation;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;

namespace WindowsCompanion_App.Services;

/// <summary>
/// Reports the local network purely so the app can choose between the internal
/// and external Home Assistant addresses.
/// </summary>
/// <remarks>
/// This deliberately does not go through the sensor catalog: route selection must
/// work whether or not the user has enabled the Wi-Fi SSID/BSSID sensors, and
/// nothing read here is ever sent to Home Assistant or written to the log. When
/// Windows refuses the SSID because the Location permission is denied, the
/// network is reported as unidentifiable rather than guessed at.
/// </remarks>
public sealed class WindowsNetworkContextProvider : INetworkContextProvider
{
    private bool _observing;

    public event Action? NetworkChanged;

    public NetworkContext GetCurrent()
    {
        var adapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .ToList();

        var vpnActive = adapters.Any(n =>
            n.NetworkInterfaceType is NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp);

        var usable = adapters
            .Where(n => n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback
                or NetworkInterfaceType.Tunnel))
            .ToList();

        if (usable.Count == 0) return NetworkContext.Offline;

        var wireless = usable.Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
        if (!wireless)
        {
            var wired = usable.Any(n => n.NetworkInterfaceType
                is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet
                or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx);

            return new NetworkContext(
                wired ? NetworkKind.Wired : NetworkKind.Unknown,
                VpnActive: vpnActive);
        }

        var wifi = WifiSensorSource.ReadConnection(
            WifiSensorSource.WifiCaptureScope.StatusOnly
            | WifiSensorSource.WifiCaptureScope.Ssid
            | WifiSensorSource.WifiCaptureScope.Bssid);
        return wifi.Status switch
        {
            WifiConnectionStatus.Connected => new NetworkContext(
                NetworkKind.Wireless,
                wifi.Ssid,
                Bssid(wifi),
                VpnActive: vpnActive),
            _ => new NetworkContext(
                NetworkKind.Wireless,
                WirelessIdentityUnavailable: true,
                VpnActive: vpnActive)
        };
    }

    /// <summary>True when Windows is withholding Wi-Fi identifiers from this app.</summary>
    public static bool WirelessIdentifiersBlocked() =>
        WifiSensorSource.ReadConnection(WifiSensorSource.WifiCaptureScope.StatusOnly).Status
        == WifiConnectionStatus.PermissionRequired;

    private static string? Bssid(WifiConnectionInfo info) =>
        info.Bssid is { Length: 6 }
            ? string.Join(":", info.Bssid.Select(value => value.ToString("X2")))
            : null;

    public void Start()
    {
        if (_observing) return;
        NetworkChange.NetworkAddressChanged += OnChanged;
        NetworkChange.NetworkAvailabilityChanged += OnChanged;
        _observing = true;
    }

    public void Stop()
    {
        if (!_observing) return;
        NetworkChange.NetworkAddressChanged -= OnChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnChanged;
        _observing = false;
    }

    private void OnChanged(object? sender, EventArgs e) => NetworkChanged?.Invoke();
}
