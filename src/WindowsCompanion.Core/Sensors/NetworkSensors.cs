namespace WindowsCompanion.Core.Sensors;

/// <summary>
/// How much of the machine's network state a reading needs. Enumerating adapters is
/// cheap but reading addresses and hardware addresses is collection of identifying
/// data, so the scope is decided from what the user has actually switched on.
/// </summary>
public enum NetworkCaptureScope
{
    /// <summary>Nothing is enabled: do not enumerate anything at all.</summary>
    None,

    /// <summary>Adapter kinds only. No address and no hardware address is read.</summary>
    ConnectionTypeOnly,

    /// <summary>Addresses, hardware address and route resolution are needed.</summary>
    Full
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

    /// <summary>The sensors whose values are network identifiers.</summary>
    public static IReadOnlyList<string> IdentifierIds { get; } =
        [IpAddressId, Ipv6AddressId, MacAddressId];

    /// <summary>
    /// Decides what may be collected. With no network sensor enabled the answer is
    /// <see cref="NetworkCaptureScope.None"/>, so nothing is enumerated and no route
    /// is probed; connection type on its own never reaches an identifier.
    /// </summary>
    public static NetworkCaptureScope ScopeFor(IReadOnlySet<string>? enabled)
    {
        if (enabled is null || enabled.Count == 0) return NetworkCaptureScope.None;
        if (IdentifierIds.Any(enabled.Contains)) return NetworkCaptureScope.Full;
        return enabled.Contains(ConnectionTypeId)
            ? NetworkCaptureScope.ConnectionTypeOnly
            : NetworkCaptureScope.None;
    }
}
