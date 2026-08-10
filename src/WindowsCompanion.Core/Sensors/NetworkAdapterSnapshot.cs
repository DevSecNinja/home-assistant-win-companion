namespace WindowsCompanion.Core.Sensors;

/// <summary>
/// Windows' duplicate-address-detection verdict for an IPv6 address, reduced to the
/// three outcomes that matter when picking an address to report.
/// </summary>
public enum Ipv6AddressState
{
    /// <summary>Valid and usable for new connections.</summary>
    Preferred,

    /// <summary>Still valid for existing connections but on its way out.</summary>
    Deprecated,

    /// <summary>Tentative, duplicate or invalid: never reportable.</summary>
    Invalid
}

/// <summary>Whether an address is stable or a rotating privacy address.</summary>
public enum Ipv6AddressOrigin
{
    /// <summary>Derived from the interface identifier or DHCPv6; survives rotation.</summary>
    Stable,

    /// <summary>An RFC 4941 temporary address that changes on every rotation.</summary>
    Temporary
}

/// <summary>One IPv6 address as the OS reports it, before any preference is applied.</summary>
public sealed record Ipv6AddressInfo(
    string Address,
    Ipv6AddressState State = Ipv6AddressState.Preferred,
    Ipv6AddressOrigin Origin = Ipv6AddressOrigin.Stable);

/// <summary>
/// A single OS network adapter reduced to the facts the sensors need. Capturing a
/// snapshot once per read is what keeps connection type, IPv4, IPv6 and MAC
/// consistent with each other instead of each enumerating adapters on its own.
/// </summary>
/// <remarks>
/// <paramref name="PhysicalAddress"/> and the address lists are only populated when
/// a sensor that needs them is enabled; a snapshot taken for connection type alone
/// carries no network identifiers at all.
/// </remarks>
public sealed record NetworkAdapterSnapshot(
    string Id,
    string Description,
    NetworkAdapterKind Kind,
    bool IsUp,
    bool IsVirtual = false,
    bool HasGateway = false,
    IReadOnlyList<string>? Ipv4Addresses = null,
    IReadOnlyList<Ipv6AddressInfo>? Ipv6Addresses = null,
    IReadOnlyList<byte>? PhysicalAddress = null,
    string? GatewayAddress = null,
    IReadOnlyList<string>? DnsAddresses = null)
{
    public IReadOnlyList<string> Ipv4 => Ipv4Addresses ?? [];

    public IReadOnlyList<Ipv6AddressInfo> Ipv6 => Ipv6Addresses ?? [];

    public IReadOnlyList<string> Dns => DnsAddresses ?? [];

    /// <summary>
    /// A real LAN adapter: Ethernet or Wi-Fi hardware rather than a tunnel, a
    /// loopback or a virtual switch created by Hyper-V, WSL, VPN or a hypervisor.
    /// </summary>
    public bool IsPhysicalLan =>
        Kind is (NetworkAdapterKind.Wired or NetworkAdapterKind.Wireless) && !IsVirtual;
}
