using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;

namespace WindowsCompanion_App.Services;

/// <summary>
/// Reports the PC's network context: connection type, local IPv4 and IPv6 address,
/// hardware addresses, default gateway and DNS servers. Updates are driven by
/// network change events rather than polling.
/// </summary>
/// <remarks>
/// Every reading comes from a single adapter snapshot taken per read, so the
/// sensors always describe the same connection instead of each enumerating adapters
/// and reaching its own conclusion. Nothing is enumerated unless a sensor that needs
/// it is switched on, and the identifying values are never logged.
///
/// Wi-Fi SSID and BSSID live in <see cref="WifiSensorSource"/>: Windows gates them
/// behind the Location capability, which this source deliberately does not touch.
/// </remarks>
public sealed class NetworkSensorSource : ISensorSource
{
    public const string ConnectionTypeId = NetworkSensors.ConnectionTypeId;
    public const string IpAddressId = NetworkSensors.IpAddressId;
    public const string Ipv6AddressId = NetworkSensors.Ipv6AddressId;
    public const string MacAddressId = NetworkSensors.MacAddressId;
    public const string LanMacAddressId = NetworkSensors.LanMacAddressId;
    public const string WlanMacAddressId = NetworkSensors.WlanMacAddressId;
    public const string GatewayAddressId = NetworkSensors.GatewayAddressId;
    public const string DnsServersId = NetworkSensors.DnsServersId;

    private const string OptInPlaceholder = "Enable to read network identifiers";

    private readonly SensorPreferences _preferences;
    private readonly NetworkIdentityMonitor _monitor;

    public NetworkSensorSource(SensorPreferences preferences)
        : this(preferences, new SystemNetworkChangeWatcher())
    {
    }

    public NetworkSensorSource(SensorPreferences preferences, INetworkChangeWatcher watcher)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _monitor = new NetworkIdentityMonitor(watcher, Capture, CurrentScope);
    }

    public IReadOnlyList<SensorDefinition> Definitions { get; } =
    [
        new(
            ConnectionTypeId,
            "Connection Type",
            "Whether this PC is on Wi-Fi, Ethernet or offline.",
            SensorPrivacy.Benign,
            EnabledByDefault: false,
            ResourceUsage: "Low. Checks this PC only when Windows reports a network change. Sends "
                           + "an extra update only if the connection details changed.",
            AutomationIdea: "When Ethernet connects, activate the desk setup."),
        new(
            IpAddressId,
            "IP Address",
            "This PC's local IPv4 address on your network.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false,
            ResourceUsage: "Low. Checks this PC only after a network change. It does not contact "
                           + "an internet service to find the address.",
            OptInPlaceholder: OptInPlaceholder),
        new(
            Ipv6AddressId,
            "IPv6 Address",
            "This PC's IPv6 address on the active network adapter. Usually globally "
            + "routable, so it can identify this PC on the internet.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false,
            ResourceUsage: "Low. Checks this PC only after a network change. It does not contact "
                           + "an internet service to find the address.",
            OptInPlaceholder: OptInPlaceholder),
        new(
            MacAddressId,
            "MAC Address",
            "The hardware address of the network adapter this PC is connected "
            + "through. A stable identifier for this machine on your network.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false,
            ResourceUsage: "Low. Reads the address from this PC only after a network change. It "
                           + "does not send network traffic to discover it.",
            OptInPlaceholder: OptInPlaceholder),
        new(
            LanMacAddressId,
            "LAN MAC Address",
            "The physical hardware address of this PC's wired Ethernet adapter.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false,
            ResourceUsage: "Low. Reads the address from this PC only after a network change. It "
                           + "does not send network traffic to discover it.",
            OptInPlaceholder: OptInPlaceholder),
        new(
            WlanMacAddressId,
            "WLAN MAC Address",
            "The permanent hardware address of this PC's Wi-Fi adapter, separate "
            + "from any randomized address Windows uses for the current network.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false,
            ResourceUsage: "Low. Reads the address from this PC only after a network change. It "
                           + "does not send network traffic to discover it.",
            OptInPlaceholder: OptInPlaceholder),
        new(
            GatewayAddressId,
            "Default Gateway",
            "The router address this PC uses to reach other networks.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false,
            ResourceUsage: "Low. Checks this PC only after a network change. It does not contact "
                           + "an internet service to find the address.",
            OptInPlaceholder: OptInPlaceholder),
        new(
            DnsServersId,
            "DNS Servers",
            "The DNS resolver addresses configured on this PC's active network adapter.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false,
            ResourceUsage: "Low. Checks this PC only after a network change. It does not contact "
                           + "an internet service to find the addresses.",
            OptInPlaceholder: OptInPlaceholder)
    ];

    public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context)
    {
        var scope = NetworkSensors.ScopeFor(enabled);
        if (scope == NetworkCaptureScope.None) return [];

        var identity = _monitor.Read(scope);
        var readings = new List<Sensor>();

        if (enabled.Contains(ConnectionTypeId))
        {
            readings.Add(new Sensor
            {
                UniqueId = ConnectionTypeId,
                Type = "sensor",
                Name = "Connection Type",
                State = identity.ConnectionType,
                Icon = NetworkClassifier.IconFor(identity.ConnectionType)
            });
        }

        if (enabled.Contains(IpAddressId))
        {
            readings.Add(new Sensor
            {
                UniqueId = IpAddressId,
                Type = "sensor",
                Name = "IP Address",
                State = identity.Ipv4Address,
                EntityCategory = "diagnostic",
                Icon = "mdi:ip-network"
            });
        }

        if (enabled.Contains(Ipv6AddressId))
        {
            readings.Add(new Sensor
            {
                UniqueId = Ipv6AddressId,
                Type = "sensor",
                Name = "IPv6 Address",
                State = identity.Ipv6Address,
                EntityCategory = "diagnostic",
                Icon = "mdi:ip-network-outline"
            });
        }

        if (enabled.Contains(MacAddressId))
        {
            readings.Add(new Sensor
            {
                UniqueId = MacAddressId,
                Type = "sensor",
                Name = "MAC Address",
                State = identity.MacAddress,
                EntityCategory = "diagnostic",
                Icon = "mdi:lan"
            });
        }

        if (enabled.Contains(LanMacAddressId))
        {
            readings.Add(new Sensor
            {
                UniqueId = LanMacAddressId,
                Type = "sensor",
                Name = "LAN MAC Address",
                State = identity.LanMacAddress,
                EntityCategory = "diagnostic",
                Icon = "mdi:ethernet"
            });
        }

        if (enabled.Contains(WlanMacAddressId))
        {
            readings.Add(new Sensor
            {
                UniqueId = WlanMacAddressId,
                Type = "sensor",
                Name = "WLAN MAC Address",
                State = identity.WlanMacAddress,
                EntityCategory = "diagnostic",
                Icon = "mdi:wifi"
            });
        }

        if (enabled.Contains(GatewayAddressId))
        {
            readings.Add(new Sensor
            {
                UniqueId = GatewayAddressId,
                Type = "sensor",
                Name = "Default Gateway",
                State = identity.GatewayAddress,
                EntityCategory = "diagnostic",
                Icon = "mdi:router-network"
            });
        }

        if (enabled.Contains(DnsServersId))
        {
            readings.Add(new Sensor
            {
                UniqueId = DnsServersId,
                Type = "sensor",
                Name = "DNS Servers",
                State = identity.DnsServers,
                EntityCategory = "diagnostic",
                Icon = "mdi:dns"
            });
        }

        return readings;
    }

    /// <summary>
    /// Previews only what the user has already opted into. A sensitive identifier is
    /// not collected - not even locally - until its own sensor is switched on, and
    /// enabling one never reveals another.
    /// </summary>
    public ValueTask<IReadOnlyList<Sensor>> PreviewAsync(
        IReadOnlySet<string> requested,
        CancellationToken cancellationToken = default)
    {
        var permitted = SensorPreviewGate.Permitted(Definitions, requested, _preferences);

        var readings = Read(permitted, new SensorReadContext("Preview")).ToList();

        foreach (var definition in Definitions)
        {
            if (requested.Contains(definition.UniqueId) && !permitted.Contains(definition.UniqueId))
            {
                readings.Add(new Sensor
                {
                    UniqueId = definition.UniqueId,
                    Name = definition.Name,
                    State = OptInPlaceholder
                });
            }
        }

        return ValueTask.FromResult<IReadOnlyList<Sensor>>(readings);
    }

    public void Start(Action onChanged) => _monitor.Start(onChanged);

    public void Stop() => _monitor.Stop();

    /// <summary>What the user's current choices allow this source to collect.</summary>
    private NetworkCaptureScope CurrentScope() =>
        NetworkSensors.ScopeFor(
            Definitions.Where(_preferences.IsEnabled)
                .Select(definition => definition.UniqueId)
                .ToHashSet(StringComparer.Ordinal));

    /// <summary>
    /// Takes one snapshot of the machine's adapters and reduces it to sensor states.
    /// The scope independently gates IP, current/permanent MAC, gateway and DNS
    /// fields, so enabling one sensitive sensor never collects another's value.
    /// </summary>
    private static NetworkIdentity Capture(NetworkCaptureScope scope)
    {
        if (scope == NetworkCaptureScope.None) return NetworkIdentity.NotConnected;

        var includeIpv4 = scope.HasFlag(NetworkCaptureScope.Ipv4Address);
        var includeIpv6 = scope.HasFlag(NetworkCaptureScope.Ipv6Address);

        try
        {
            var adapters = NetworkInterface.GetAllNetworkInterfaces()
                .Select(adapter => Describe(adapter, scope))
                .ToList();

            if (scope == NetworkCaptureScope.ConnectionTypeOnly)
            {
                return NetworkIdentity.NotConnected with
                {
                    ConnectionType = NetworkClassifier.ClassifyAdapters(adapters)
                };
            }

            return NetworkIdentity.From(
                adapters,
                includeIpv4 ? ResolveRoute(AddressFamily.InterNetwork) : null,
                includeIpv6 ? ResolveRoute(AddressFamily.InterNetworkV6) : null);
        }
        catch (NetworkInformationException)
        {
            return NetworkIdentity.NotConnected;
        }
    }

    private static NetworkAdapterSnapshot Describe(
        NetworkInterface adapter,
        NetworkCaptureScope scope)
    {
        var kind = adapter.NetworkInterfaceType switch
        {
            NetworkInterfaceType.Wireless80211 => NetworkAdapterKind.Wireless,
            NetworkInterfaceType.Loopback => NetworkAdapterKind.Loopback,
            NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp => NetworkAdapterKind.Tunnel,
            NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet
                or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx
                => NetworkAdapterKind.Wired,
            _ => NetworkAdapterKind.Other
        };

        var isUp = adapter.OperationalStatus == OperationalStatus.Up;
        var isVirtual = NetworkAdapterSelector.LooksVirtual(adapter.Description)
                        || NetworkAdapterSelector.LooksVirtual(adapter.Name);

        var includeIpv4 = scope.HasFlag(NetworkCaptureScope.Ipv4Address);
        var includeIpv6 = scope.HasFlag(NetworkCaptureScope.Ipv6Address);
        if (scope == NetworkCaptureScope.ConnectionTypeOnly)
            return new NetworkAdapterSnapshot(adapter.Id, adapter.Description, kind, isUp, isVirtual);

        var includeCurrentPhysicalAddress =
            scope.HasFlag(NetworkCaptureScope.CurrentPhysicalAddress);
        var includePermanentPhysicalAddress =
            !isVirtual
            && (kind == NetworkAdapterKind.Wired
                    && scope.HasFlag(NetworkCaptureScope.LanPermanentAddress)
                || kind == NetworkAdapterKind.Wireless
                    && scope.HasFlag(NetworkCaptureScope.WlanPermanentAddress));
        var includeGatewayAddress = scope.HasFlag(NetworkCaptureScope.GatewayAddress);
        var includeDnsServers = scope.HasFlag(NetworkCaptureScope.DnsServers);
        var ipv4 = new List<string>();
        var ipv6 = new List<Ipv6AddressInfo>();
        var hasGateway = false;
        string? gatewayAddress = null;
        var dns = new List<string>();

        if (includeIpv4 || includeIpv6 || includeGatewayAddress || includeDnsServers)
        {
            try
            {
                var properties = adapter.GetIPProperties();
                var gateways = properties.GatewayAddresses
                    .Where(gateway => gateway.Address is not null && !IsUnspecified(gateway.Address))
                    .ToList();
                hasGateway = gateways.Count > 0;

                if (includeGatewayAddress)
                    gatewayAddress = gateways.FirstOrDefault()?.Address.ToString();

                if (includeDnsServers)
                {
                    dns = properties.DnsAddresses
                        .Where(address => !IsUnspecified(address))
                        .Select(address => address.ToString())
                        .ToList();
                }

                if (includeIpv4 || includeIpv6)
                {
                    foreach (var unicast in properties.UnicastAddresses)
                    {
                        if (includeIpv4
                            && unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            ipv4.Add(unicast.Address.ToString());
                        }
                        else if (includeIpv6
                                 && unicast.Address.AddressFamily == AddressFamily.InterNetworkV6)
                        {
                            ipv6.Add(new Ipv6AddressInfo(
                                unicast.Address.ToString(),
                                StateOf(unicast),
                                OriginOf(unicast)));
                        }
                    }
                }
            }
            catch (NetworkInformationException)
            {
                // An adapter can disappear mid-enumeration; describe it without addresses.
            }
            catch (PlatformNotSupportedException)
            {
            }
        }

        return new NetworkAdapterSnapshot(
            adapter.Id,
            adapter.Description,
            kind,
            isUp,
            isVirtual,
            hasGateway,
            ipv4,
            ipv6,
            includeCurrentPhysicalAddress ? PhysicalAddressOf(adapter) : null,
            gatewayAddress,
            dns,
            includePermanentPhysicalAddress
                ? WindowsNetworkInterfaceIdentity.PermanentPhysicalAddressOf(adapter.Id)
                : null);
    }

    private static bool IsUnspecified(IPAddress address) =>
        address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any);

    private static Ipv6AddressState StateOf(UnicastIPAddressInformation address)
    {
        try
        {
            return address.DuplicateAddressDetectionState switch
            {
                DuplicateAddressDetectionState.Preferred => Ipv6AddressState.Preferred,
                DuplicateAddressDetectionState.Deprecated => Ipv6AddressState.Deprecated,
                _ => Ipv6AddressState.Invalid
            };
        }
        catch (PlatformNotSupportedException)
        {
            return Ipv6AddressState.Preferred;
        }
    }

    private static Ipv6AddressOrigin OriginOf(UnicastIPAddressInformation address)
    {
        try
        {
            return address.SuffixOrigin == SuffixOrigin.Random
                ? Ipv6AddressOrigin.Temporary
                : Ipv6AddressOrigin.Stable;
        }
        catch (PlatformNotSupportedException)
        {
            return Ipv6AddressOrigin.Stable;
        }
    }

    private static byte[]? PhysicalAddressOf(NetworkInterface adapter)
    {
        try
        {
            var bytes = adapter.GetPhysicalAddress().GetAddressBytes();
            return bytes.Length == 0 ? null : bytes;
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Asks Windows which local endpoint would carry traffic for the given family.
    /// Connecting a UDP socket only resolves the route: no packet is ever sent and
    /// the destination addresses are never contacted. The socket is released whether
    /// or not the lookup succeeds.
    /// </summary>
    private static string? ResolveRoute(AddressFamily family)
    {
        var destination = family == AddressFamily.InterNetwork
            ? "8.8.8.8"
            : "2001:4860:4860::8888";

        return NetworkRouteProbe.Resolve(
            () => new Socket(family, SocketType.Dgram, ProtocolType.Udp),
            socket =>
            {
                socket.Connect(destination, 65530);
                return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString();
            });
    }
}
