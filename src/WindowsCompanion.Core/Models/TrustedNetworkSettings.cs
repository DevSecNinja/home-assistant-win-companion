namespace WindowsCompanion.Core.Models;

/// <summary>
/// The networks on which the internal Home Assistant address is considered
/// appropriate. Local configuration only: these identifiers are never sent to
/// Home Assistant and never written to the log.
/// </summary>
public sealed class TrustedNetworkSettings
{
    /// <summary>
    /// IPv4 and IPv6 network blocks whose connected interface addresses make the
    /// internal route eligible. Entries are canonical address/prefix strings.
    /// </summary>
    public List<string> Cidrs { get; set; } = new();

    /// <summary>Wi-Fi network names the user marked as their own network.</summary>
    public List<string> Ssids { get; set; } = new();

    /// <summary>
    /// Optional access point identifiers. Empty for most users: mesh networks
    /// present many BSSIDs under one SSID, and a BSSID is precise location data,
    /// so it is only stored when the user explicitly opts in.
    /// </summary>
    public List<string> Bssids { get; set; } = new();

    /// <summary>
    /// Requires the access point to match as well as the network name. Off by
    /// default so mesh Wi-Fi does not fail the trust check when roaming.
    /// </summary>
    public bool RequireBssidMatch { get; set; }

    /// <summary>
    /// Treats any wired connection as the home LAN. This is the desktop/dock
    /// strategy: Windows exposes no SSID for Ethernet, so there is nothing else
    /// to match on.
    /// </summary>
    public bool TrustWiredNetworks { get; set; }

    /// <summary>
    /// Allows the internal address to be probed as a last resort on a network
    /// that could not be identified. Off by default so an untrusted network never
    /// sees the internal hostname.
    /// </summary>
    public bool ProbeInternalOnUnknownNetworks { get; set; }

    public bool IsConfigured => Cidrs.Count > 0 || Ssids.Count > 0 || TrustWiredNetworks;

    public bool HasValidCidrs
    {
        get
        {
            var validation = TrustedNetworkCidr.Validate(Cidrs);
            return validation.IsValid && validation.CanonicalCidrs.Count > 0;
        }
    }

    /// <summary>
    /// Decides whether the internal address belongs on this network.
    /// </summary>
    /// <remarks>
    /// A Wi-Fi network whose name Windows will not disclose can never be trusted,
    /// because trusting it would mean probing the internal address on any unknown
    /// network. The UI explains the Location permission instead.
    /// </remarks>
    public bool Trusts(NetworkContext network)
    {
        ArgumentNullException.ThrowIfNull(network);

        if (TrustedNetworkCidr.Matches(Cidrs, network.Addresses)) return true;

        return network.Kind switch
        {
            NetworkKind.Wired => TrustWiredNetworks,
            NetworkKind.Wireless => TrustsWireless(network),
            _ => false
        };
    }

    private bool TrustsWireless(NetworkContext network)
    {
        if (string.IsNullOrEmpty(network.Ssid)) return false;
        if (!Ssids.Contains(network.Ssid, StringComparer.Ordinal)) return false;
        if (!RequireBssidMatch || Bssids.Count == 0) return true;

        return network.Bssid is { Length: > 0 }
               && Bssids.Contains(network.Bssid, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates a detached copy whose CIDRs have passed validation and use
    /// canonical text. Invalid drafts must never reach persistence.
    /// </summary>
    public TrustedNetworkSettings ValidatedCopy()
    {
        var validation = TrustedNetworkCidr.Validate(Cidrs);
        if (!validation.IsValid)
            throw new InvalidOperationException(validation.Errors[0].Message);

        return new TrustedNetworkSettings
        {
            Cidrs = [.. validation.CanonicalCidrs],
            Ssids = [.. Ssids],
            Bssids = [.. Bssids],
            RequireBssidMatch = RequireBssidMatch,
            TrustWiredNetworks = TrustWiredNetworks,
            ProbeInternalOnUnknownNetworks = ProbeInternalOnUnknownNetworks
        };
    }
}
