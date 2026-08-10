namespace WindowsCompanion.Core.Models;

/// <summary>How the machine is attached to a network right now.</summary>
public enum NetworkKind
{
    /// <summary>No usable adapter; nothing can be reached.</summary>
    Offline,
    Wireless,
    Wired,

    /// <summary>Connected, but the kind could not be determined.</summary>
    Unknown
}

/// <summary>
/// A local, never-transmitted snapshot of the current network used purely to
/// decide which Home Assistant address to try.
/// </summary>
/// <remarks>
/// This is deliberately independent of the Wi-Fi SSID/BSSID sensors: route
/// selection is local functionality and must work without publishing network
/// identifiers to Home Assistant. Nothing in here is ever logged.
/// </remarks>
/// <param name="Kind">How the machine is attached to the network.</param>
/// <param name="Ssid">Wi-Fi network name, when Windows allows reading it.</param>
/// <param name="Bssid">Access point identifier, when available. Precise location data.</param>
/// <param name="WirelessIdentityUnavailable">
/// True when the machine is on Wi-Fi but Windows would not disclose the SSID
/// (typically because the Location permission is denied). The network is then
/// treated as unidentifiable rather than untrusted.
/// </param>
/// <param name="VpnActive">A tunnel adapter is up, so reachability is ambiguous.</param>
/// <param name="LocalAddresses">
/// IPv4 and IPv6 addresses on active non-tunnel interfaces. These stay local and
/// are used only to match user-configured CIDR blocks.
/// </param>
public sealed record NetworkContext(
    NetworkKind Kind,
    string? Ssid = null,
    string? Bssid = null,
    bool WirelessIdentityUnavailable = false,
    bool VpnActive = false,
    IReadOnlyList<string>? LocalAddresses = null)
{
    public static NetworkContext Offline { get; } = new(NetworkKind.Offline);

    public IReadOnlyList<string> Addresses => LocalAddresses ?? [];

    /// <summary>True when nothing about this network can be used to identify it.</summary>
    public bool IsIdentifiable =>
        Addresses.Count > 0
        || Kind == NetworkKind.Wired
        || (Kind == NetworkKind.Wireless && !string.IsNullOrEmpty(Ssid));

    /// <summary>
    /// Whether two snapshots describe the same routing profile. Adapter events often
    /// repeat with a new list instance, which must not count as a new network.
    /// </summary>
    public bool HasSameRoutingProfile(NetworkContext other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Kind == other.Kind
               && string.Equals(Ssid, other.Ssid, StringComparison.Ordinal)
               && string.Equals(Bssid, other.Bssid, StringComparison.OrdinalIgnoreCase)
               && WirelessIdentityUnavailable == other.WirelessIdentityUnavailable
               && VpnActive == other.VpnActive
               && Addresses.Order(StringComparer.Ordinal)
                   .SequenceEqual(other.Addresses.Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }
}

/// <summary>Supplies the current <see cref="NetworkContext"/> and change notifications.</summary>
public interface INetworkContextProvider
{
    NetworkContext GetCurrent();

    /// <summary>Raised when Windows reports an address or availability change.</summary>
    event Action? NetworkChanged;

    void Start();
    void Stop();
}
