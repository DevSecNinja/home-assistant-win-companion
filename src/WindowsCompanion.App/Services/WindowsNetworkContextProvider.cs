using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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
                or NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp))
            .ToList();

        if (usable.Count == 0) return NetworkContext.Offline;

        var lanAdapters = usable.Where(IsPhysicalLan).ToList();
        var localAddresses = LocalAddresses(lanAdapters);
        var wireless = lanAdapters.Any(n =>
            n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
        if (!wireless)
        {
            var wired = lanAdapters.Any(n => n.NetworkInterfaceType
                is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet
                or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx);

            return new NetworkContext(
                wired ? NetworkKind.Wired : NetworkKind.Unknown,
                VpnActive: vpnActive,
                LocalAddresses: localAddresses);
        }

        var wifi = WifiSensorSource.ReadConnection();
        return wifi.Status switch
        {
            WifiConnectionStatus.Connected => new NetworkContext(
                NetworkKind.Wireless,
                wifi.Ssid,
                Bssid(wifi),
                VpnActive: vpnActive,
                LocalAddresses: localAddresses),
            _ => new NetworkContext(
                NetworkKind.Wireless,
                WirelessIdentityUnavailable: true,
                VpnActive: vpnActive,
                LocalAddresses: localAddresses)
        };
    }

    /// <summary>True when Windows is withholding Wi-Fi identifiers from this app.</summary>
    public static bool WirelessIdentifiersBlocked() =>
        WifiSensorSource.ReadConnection().Status == WifiConnectionStatus.PermissionRequired;

    private static string? Bssid(WifiConnectionInfo info) =>
        info.Bssid is { Length: 6 }
            ? string.Join(":", info.Bssid.Select(value => value.ToString("X2")))
            : null;

    private static IReadOnlyList<string> LocalAddresses(IEnumerable<NetworkInterface> adapters)
    {
        var addresses = new HashSet<string>(StringComparer.Ordinal);

        foreach (var adapter in adapters)
        {
            try
            {
                foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
                {
                    var address = unicast.Address;
                    if (address.AddressFamily is not (AddressFamily.InterNetwork
                            or AddressFamily.InterNetworkV6)
                        || IPAddress.IsLoopback(address)
                        || address.Equals(IPAddress.Any)
                        || address.Equals(IPAddress.IPv6Any))
                    {
                        continue;
                    }

                    addresses.Add(address.ToString());
                }
            }
            catch (NetworkInformationException)
            {
                // An interface can disappear while Windows is enumerating it.
            }
            catch (PlatformNotSupportedException)
            {
            }
        }

        return addresses.Order(StringComparer.Ordinal).ToList();
    }

    private static bool IsPhysicalLan(NetworkInterface adapter)
    {
        var isLanType = adapter.NetworkInterfaceType
            is NetworkInterfaceType.Wireless80211
            or NetworkInterfaceType.Ethernet
            or NetworkInterfaceType.GigabitEthernet
            or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.FastEthernetFx;
        return isLanType
               && !NetworkAdapterSelector.LooksVirtual(adapter.Name)
               && !NetworkAdapterSelector.LooksVirtual(adapter.Description);
    }

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
