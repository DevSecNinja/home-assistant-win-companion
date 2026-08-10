using System.Net;

namespace WindowsCompanion.Core.Sensors;

/// <summary>
/// Picks the adapter whose identity the network sensors describe, so connection
/// type, IPv4, IPv6 and MAC all talk about the same physical connection.
/// </summary>
public static class NetworkAdapterSelector
{
    private static readonly string[] VirtualMarkers =
    [
        "hyper-v",
        "vethernet",
        "virtual",
        "vmware",
        "virtualbox",
        "vbox",
        "wsl",
        "docker",
        "loopback",
        "pseudo",
        "tap-",
        "tap adapter",
        "tun",
        "vpn",
        "wireguard",
        "tailscale",
        "zerotier",
        "openvpn",
        "anyconnect",
        "wintun",
        "bluetooth"
    ];

    /// <summary>
    /// Recognises adapters created by virtualisation, containers or VPN clients from
    /// their description. Windows reports most of them as Ethernet, so the interface
    /// type alone cannot tell a docking station from a Hyper-V switch.
    /// </summary>
    public static bool LooksVirtual(string? description) =>
        !string.IsNullOrWhiteSpace(description)
        && VirtualMarkers.Any(marker =>
            description.Contains(marker, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Selects the adapter carrying the active LAN route.
    /// </summary>
    /// <remarks>
    /// The route hints are the local endpoints Windows would use to reach the
    /// network. When they resolve to real Ethernet or Wi-Fi hardware that adapter
    /// wins outright. When they resolve to a VPN or virtual adapter the physical LAN
    /// adapter is preferred instead: these sensors describe the PC's own network
    /// attachment, not whichever tunnel happens to be up. Only when no physical LAN
    /// adapter is usable at all does a tunnel or virtual adapter get reported.
    /// </remarks>
    public static NetworkAdapterSnapshot? SelectActive(
        IEnumerable<NetworkAdapterSnapshot>? adapters,
        string? routeLocalIpv4 = null,
        string? routeLocalIpv6 = null)
    {
        if (adapters is null) return null;

        var usable = adapters
            .Where(a => a.IsUp && a.Kind != NetworkAdapterKind.Loopback)
            .ToList();

        if (usable.Count == 0) return null;

        var routeMatch = usable.FirstOrDefault(a => a.MatchesActiveRoute)
                         ?? usable.FirstOrDefault(a =>
                             MatchesRoute(a, routeLocalIpv4, routeLocalIpv6));
        if (routeMatch is { IsPhysicalLan: true }) return routeMatch;

        var physical = usable.Where(a => a.IsPhysicalLan).OrderBy(Preference).FirstOrDefault();
        return physical ?? routeMatch ?? usable.OrderBy(Preference).FirstOrDefault();
    }

    private static bool MatchesRoute(
        NetworkAdapterSnapshot adapter, string? routeLocalIpv4, string? routeLocalIpv6)
    {
        if (!string.IsNullOrWhiteSpace(routeLocalIpv4)
            && adapter.Ipv4.Any(address => SameAddress(address, routeLocalIpv4)))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(routeLocalIpv6)
               && adapter.Ipv6.Any(address => SameAddress(address.Address, routeLocalIpv6));
    }

    private static bool SameAddress(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        return IPAddress.TryParse(Bare(left), out var a)
               && IPAddress.TryParse(Bare(right), out var b)
               && a.Equals(b);

        static string Bare(string address)
        {
            var zone = address.IndexOf('%');
            return zone >= 0 ? address[..zone] : address;
        }
    }

    /// <summary>
    /// Ranks adapters when routing gives no answer: something with a default gateway
    /// first, then Wi-Fi over wired to match how the connection type sensor
    /// describes a docked laptop, and finally a stable id so the choice never flaps.
    /// </summary>
    private static (int Gateway, int Kind, string Id) Preference(NetworkAdapterSnapshot adapter) =>
        (adapter.HasGateway ? 0 : 1,
            adapter.Kind switch
            {
                NetworkAdapterKind.Wireless => 0,
                NetworkAdapterKind.Wired => 1,
                NetworkAdapterKind.Other => 2,
                _ => 3
            },
            adapter.Id);
}

/// <summary>Formats hardware addresses the way Home Assistant and Windows show them.</summary>
public static class MacAddressFormatter
{
    /// <summary>
    /// Uppercase colon-separated bytes, for example <c>AA:BB:CC:DD:EE:FF</c>. Returns
    /// null for anything that is not a usable EUI-48: tunnel and virtual adapters
    /// commonly report an empty or all-zero address.
    /// </summary>
    public static string? Format(IReadOnlyList<byte>? address)
    {
        if (address is not { Count: 6 }) return null;
        if (address.All(b => b == 0)) return null;
        return string.Join(":", address.Select(b => b.ToString("X2")));
    }
}
