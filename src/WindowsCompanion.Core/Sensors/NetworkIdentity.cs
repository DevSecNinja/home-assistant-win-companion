using System.Net;
using System.Net.Sockets;

namespace WindowsCompanion.Core.Sensors;

/// <summary>
/// The network sensor states derived from one adapter snapshot, so connection type,
/// IPv4, IPv6 and MAC can never disagree about which connection they describe.
/// </summary>
public sealed record NetworkIdentity(
    string ConnectionType,
    string Ipv4Address,
    string Ipv6Address,
    string MacAddress,
    string LanMacAddress = NetworkClassifier.NotConnected,
    string WlanMacAddress = NetworkClassifier.NotConnected,
    string GatewayAddress = NetworkClassifier.NotConnected,
    string DnsServers = NetworkClassifier.NotConnected)
{
    public static NetworkIdentity NotConnected { get; } = new(
        NetworkClassifier.NotConnected,
        NetworkClassifier.NotConnected,
        NetworkClassifier.NotConnected,
        NetworkClassifier.NotConnected,
        NetworkClassifier.NotConnected,
        NetworkClassifier.NotConnected,
        NetworkClassifier.NotConnected,
        NetworkClassifier.NotConnected);

    /// <summary>
    /// Derives every network sensor state from the adapters the OS reports plus the
    /// local endpoints of the active default routes, if the caller resolved them.
    /// </summary>
    public static NetworkIdentity From(
        IReadOnlyList<NetworkAdapterSnapshot>? adapters,
        string? routeLocalIpv4 = null,
        string? routeLocalIpv6 = null)
    {
        if (adapters is null || adapters.Count == 0) return NotConnected;

        var connectionType = NetworkClassifier.ClassifyAdapters(adapters);
        var active = NetworkAdapterSelector.SelectActive(adapters, routeLocalIpv4, routeLocalIpv6);
        if (active is null) return NotConnected with { ConnectionType = connectionType };

        return new NetworkIdentity(
            connectionType,
            SelectIpv4(active, routeLocalIpv4) ?? NetworkClassifier.NotConnected,
            Ipv6AddressClassifier.SelectPreferred(active.Ipv6) ?? NetworkClassifier.NotConnected,
            MacAddressFormatter.Format(active.PhysicalAddress) ?? NetworkClassifier.NotConnected,
            SelectMac(adapters, NetworkAdapterKind.Wired) ?? NetworkClassifier.NotConnected,
            SelectMac(adapters, NetworkAdapterKind.Wireless) ?? NetworkClassifier.NotConnected,
            active.GatewayAddress ?? NetworkClassifier.NotConnected,
            SelectDns(active) ?? NetworkClassifier.NotConnected);
    }

    /// <summary>
    /// The hardware address of the physical LAN or Wi-Fi adapter regardless of which
    /// one is carrying the active route, so a docked laptop can still report its
    /// Wi-Fi MAC and vice versa. An adapter that is up is preferred over one that is
    /// merely present but disconnected.
    /// </summary>
    private static string? SelectMac(IReadOnlyList<NetworkAdapterSnapshot> adapters, NetworkAdapterKind kind)
    {
        var candidates = adapters.Where(a => a.IsPhysicalLan && a.Kind == kind).ToList();
        if (candidates.Count == 0) return null;

        var chosen = candidates.FirstOrDefault(a => a.IsUp) ?? candidates[0];
        return MacAddressFormatter.Format(chosen.PhysicalAddress);
    }

    /// <summary>
    /// The DNS servers configured on the active adapter, joined for display. Only
    /// the adapter's own configuration is reported, never a system-wide resolver
    /// list, so this always matches the connection the other sensors describe.
    /// </summary>
    private static string? SelectDns(NetworkAdapterSnapshot adapter)
    {
        var servers = adapter.Dns.Where(address => !string.IsNullOrWhiteSpace(address)).ToList();
        return servers.Count == 0 ? null : string.Join(", ", servers);
    }

    /// <summary>
    /// The routed address wins when it belongs to the selected adapter; otherwise the
    /// adapter's own address is used. Automatic private addresses (169.254.0.0/16)
    /// mean DHCP failed and are not worth reporting.
    /// </summary>
    private static string? SelectIpv4(NetworkAdapterSnapshot adapter, string? routeLocalIpv4)
    {
        var usable = adapter.Ipv4.Where(IsUsable).ToList();

        if (!string.IsNullOrWhiteSpace(routeLocalIpv4)
            && usable.Any(address => string.Equals(address, routeLocalIpv4, StringComparison.OrdinalIgnoreCase)))
        {
            return routeLocalIpv4;
        }

        return usable.FirstOrDefault();

        static bool IsUsable(string address)
        {
            if (!IPAddress.TryParse(address, out var parsed)
                || parsed.AddressFamily != AddressFamily.InterNetwork)
            {
                return false;
            }

            var bytes = parsed.GetAddressBytes();
            if (bytes[0] == 127) return false;
            return !(bytes[0] == 169 && bytes[1] == 254);
        }
    }
}
