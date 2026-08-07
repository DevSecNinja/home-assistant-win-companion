using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using HaCompanion.Core.Models;
using HaCompanion.Core.Sensors;

namespace HaCompanion_App.Services;

/// <summary>
/// Reports the PC's network context: connection type and local IP address.
/// Updates are driven by network change events rather than polling.
/// </summary>
/// <remarks>
/// Wi-Fi SSID and BSSID are deliberately absent. Windows gates them behind the
/// Location capability, so <c>WlanQueryInterface(current_connection)</c> returns
/// ERROR_ACCESS_DENIED for an unpackaged desktop app that cannot cleanly request
/// that permission. See the tracking issue rather than working around it.
/// </remarks>
public sealed class NetworkSensorSource : ISensorSource
{
    public const string ConnectionTypeId = "connectivity_connection_type";
    public const string IpAddressId = "ip_address";

    private const string NotConnected = "Not Connected";

    private Action? _onChanged;
    private bool _observing;

    public IReadOnlyList<SensorDefinition> Definitions { get; } = new[]
    {
        new SensorDefinition(
            ConnectionTypeId,
            "Connection Type",
            "Whether this PC is on Wi-Fi, Ethernet or offline.",
            SensorPrivacy.Benign,
            EnabledByDefault: false),
        new SensorDefinition(
            IpAddressId,
            "IP Address",
            "This PC's local IP address on your network.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false)
    };

    public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context)
    {
        var readings = new List<Sensor>();

        if (enabled.Contains(ConnectionTypeId))
        {
            var type = NetworkClassifier.Classify(GetOperationalAdapters());
            readings.Add(new Sensor
            {
                UniqueId = ConnectionTypeId,
                Type = "sensor",
                Name = "Connection Type",
                State = type,
                Icon = NetworkClassifier.IconFor(type)
            });
        }

        if (enabled.Contains(IpAddressId))
        {
            readings.Add(new Sensor
            {
                UniqueId = IpAddressId,
                Type = "sensor",
                Name = "IP Address",
                State = GetLocalIpAddress() ?? NotConnected,
                EntityCategory = "diagnostic",
                Icon = "mdi:ip-network"
            });
        }

        return readings;
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

    /// <summary>Maps Windows adapters onto the platform-agnostic kinds Core classifies.</summary>
    private static IEnumerable<NetworkAdapterKind> GetOperationalAdapters() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .Select(n => n.NetworkInterfaceType switch
            {
                NetworkInterfaceType.Wireless80211 => NetworkAdapterKind.Wireless,
                NetworkInterfaceType.Loopback => NetworkAdapterKind.Loopback,
                NetworkInterfaceType.Tunnel => NetworkAdapterKind.Tunnel,
                NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet
                    or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx
                    => NetworkAdapterKind.Wired,
                _ => NetworkAdapterKind.Other
            });

    private static string? GetLocalIpAddress()
    {
        try
        {
            // Picks the address the OS would actually use to reach the network.
            // A UDP connect only sets the route; nothing is transmitted.
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 65530);
            return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString();
        }
        catch
        {
            return null;
        }
    }
}
