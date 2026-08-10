namespace WindowsCompanion.Core.Sensors;

/// <summary>
/// How much of the machine's network state a reading needs. Enumerating adapters is
/// cheap but reading addresses and hardware addresses is collection of identifying
/// data, so the scope is decided from what the user has actually switched on.
/// </summary>
[Flags]
public enum NetworkCaptureScope
{
    /// <summary>Nothing is enabled: do not enumerate anything at all.</summary>
    None = 0,

    /// <summary>Adapter kinds only. No address and no hardware address is read.</summary>
    ConnectionTypeOnly = 1 << 0,

    /// <summary>IP addresses and route resolution are needed.</summary>
    IpAddresses = 1 << 1,

    /// <summary>The active adapter's current hardware address is needed.</summary>
    CurrentPhysicalAddress = 1 << 2,

    /// <summary>The active adapter's default gateway address is needed.</summary>
    GatewayAddress = 1 << 3,

    /// <summary>The active adapter's DNS resolver addresses are needed.</summary>
    DnsServers = 1 << 4,

    /// <summary>The wired adapter's permanent hardware address is needed.</summary>
    LanPermanentAddress = 1 << 5,

    /// <summary>The wireless adapter's permanent hardware address is needed.</summary>
    WlanPermanentAddress = 1 << 6,

    /// <summary>All network sensor fields may be collected.</summary>
    Full = ConnectionTypeOnly | IpAddresses | CurrentPhysicalAddress | GatewayAddress
           | DnsServers | LanPermanentAddress | WlanPermanentAddress
}

/// <summary>
/// Identifiers for the network sensors and the rule that decides how much may be
/// collected for a given set of enabled sensors.
/// </summary>
public static class NetworkSensors
{
    public const string ConnectionTypeId = "connectivity_connection_type";
    public const string IpAddressId = "ip_address";
    public const string Ipv6AddressId = "ipv6_address";
    public const string MacAddressId = "mac_address";
    public const string LanMacAddressId = "lan_mac_address";
    public const string WlanMacAddressId = "wlan_mac_address";
    public const string GatewayAddressId = "gateway_address";
    public const string DnsServersId = "dns_servers";

    /// <summary>The sensors whose values are network identifiers.</summary>
    public static IReadOnlyList<string> IdentifierIds { get; } =
        [
            IpAddressId, Ipv6AddressId, MacAddressId,
            LanMacAddressId, WlanMacAddressId, GatewayAddressId, DnsServersId
        ];

    /// <summary>
    /// Decides what may be collected. With no network sensor enabled the answer is
    /// <see cref="NetworkCaptureScope.None"/>, so nothing is enumerated and no route
    /// is probed; connection type on its own never reaches an identifier.
    /// </summary>
    public static NetworkCaptureScope ScopeFor(IReadOnlySet<string>? enabled)
    {
        if (enabled is null || enabled.Count == 0) return NetworkCaptureScope.None;

        var scope = enabled.Contains(ConnectionTypeId)
            ? NetworkCaptureScope.ConnectionTypeOnly
            : NetworkCaptureScope.None;

        if (enabled.Contains(IpAddressId) || enabled.Contains(Ipv6AddressId))
            scope |= NetworkCaptureScope.IpAddresses;

        if (enabled.Contains(MacAddressId))
            scope |= NetworkCaptureScope.IpAddresses | NetworkCaptureScope.CurrentPhysicalAddress;

        if (enabled.Contains(LanMacAddressId))
            scope |= NetworkCaptureScope.LanPermanentAddress;

        if (enabled.Contains(WlanMacAddressId))
            scope |= NetworkCaptureScope.WlanPermanentAddress;

        if (enabled.Contains(GatewayAddressId))
            scope |= NetworkCaptureScope.IpAddresses | NetworkCaptureScope.GatewayAddress;

        if (enabled.Contains(DnsServersId))
            scope |= NetworkCaptureScope.IpAddresses | NetworkCaptureScope.DnsServers;

        return scope;
    }
}
