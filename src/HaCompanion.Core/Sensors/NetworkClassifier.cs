namespace HaCompanion.Core.Sensors;

/// <summary>How a network adapter connects, independent of any OS type enum.</summary>
public enum NetworkAdapterKind
{
    Wireless,
    Wired,
    Loopback,
    Tunnel,
    Other
}

/// <summary>
/// Decides how the machine is connected from the set of adapters the OS reports.
/// Pure, so the selection rules can be tested without a network.
/// </summary>
public static class NetworkClassifier
{
    public const string NotConnected = "Not Connected";
    public const string WiFi = "Wi-Fi";
    public const string Ethernet = "Ethernet";

    /// <summary>
    /// Classifies from the adapters that are currently operational. Loopback and
    /// tunnel adapters are ignored: they are always up and would make an offline
    /// machine look connected. Wi-Fi wins over wired because a laptop docked and
    /// also on Wi-Fi is usually described by its wireless network.
    /// </summary>
    public static string Classify(IEnumerable<NetworkAdapterKind> operationalAdapters)
    {
        var usable = operationalAdapters
            .Where(k => k is not (NetworkAdapterKind.Loopback or NetworkAdapterKind.Tunnel))
            .ToList();

        if (usable.Contains(NetworkAdapterKind.Wireless)) return WiFi;
        return usable.Count > 0 ? Ethernet : NotConnected;
    }

    public static string IconFor(string connectionType) => connectionType switch
    {
        WiFi => "mdi:wifi",
        Ethernet => "mdi:ethernet-cable",
        _ => "mdi:network-off"
    };
}
