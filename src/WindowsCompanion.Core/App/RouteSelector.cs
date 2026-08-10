using WindowsCompanion.Core.Models;

namespace WindowsCompanion.Core.App;

/// <summary>How much the current network says about where Home Assistant is.</summary>
public enum NetworkTrust
{
    /// <summary>A network the user marked as their own.</summary>
    Trusted,

    /// <summary>A network that can be identified and is not the user's own.</summary>
    Untrusted,

    /// <summary>
    /// Connected, but nothing usable identifies the network - no trusted networks
    /// configured, Windows withholding the SSID, or a VPN in the way.
    /// </summary>
    Unidentifiable,

    Offline
}

/// <param name="Candidates">Routes to try, best first. Empty means "do not connect".</param>
public sealed record RoutePlan(
    IReadOnlyList<RouteKind> Candidates,
    NetworkTrust Trust,
    string Reason);

/// <summary>
/// Decides, from the connection mode and the local network only, which addresses
/// may be tried and in what order. Pure so every network situation the issue
/// calls out can be tested without a network.
/// </summary>
public static class RouteSelector
{
    public static NetworkTrust Classify(TrustedNetworkSettings settings, NetworkContext network)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(network);

        if (network.Kind == NetworkKind.Offline) return NetworkTrust.Offline;
        if (settings.Trusts(network)) return NetworkTrust.Trusted;

        // A tunnel can carry the internal address and can equally hide which
        // network is underneath, so a VPN is never evidence either way.
        if (network.VpnActive) return NetworkTrust.Unidentifiable;

        // Without a trusted network the user has not opted into local routing at
        // all; treating that as "untrusted" would silently pin everyone to the
        // external address.
        if (!settings.IsConfigured) return NetworkTrust.Unidentifiable;

        if (network.Kind == NetworkKind.Unknown
            && (!settings.HasValidCidrs || network.Addresses.Count == 0))
            return NetworkTrust.Unidentifiable;

        // On Wi-Fi that Windows will not name (Location denied), the network is
        // still identifiable when a connected address can be compared with CIDRs.
        if (network.Kind == NetworkKind.Wireless
            && string.IsNullOrEmpty(network.Ssid)
            && (!settings.HasValidCidrs || network.Addresses.Count == 0))
            return NetworkTrust.Unidentifiable;

        return NetworkTrust.Untrusted;
    }

    public static RoutePlan Plan(ServerConfig config, NetworkContext network)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(network);

        var trust = Classify(config.TrustedNetworks, network);
        var available = config.ConfiguredRoutes();

        if (available.Count == 0)
            return new RoutePlan([], trust, "No internal or external address is configured.");

        var (preferred, reason) = Preference(config, trust);
        var candidates = preferred.Where(available.Contains).ToList();

        // A single configured address is the whole configuration; refusing to use
        // it would leave the app permanently offline rather than merely cautious.
        if (candidates.Count == 0 && trust != NetworkTrust.Offline)
        {
            candidates = available.ToList();
            reason = "Only one address is configured, so it is used regardless of network.";
        }

        return new RoutePlan(candidates, trust, reason);
    }

    private static (IReadOnlyList<RouteKind> Routes, string Reason) Preference(
        ServerConfig config, NetworkTrust trust) =>
        config.ConnectionMode switch
        {
            ConnectionMode.InternalOnly =>
                ([RouteKind.Internal], "Internal only."),
            ConnectionMode.ExternalOnly =>
                ([RouteKind.External], "External only."),
            ConnectionMode.PreferInternal =>
                ([RouteKind.Internal, RouteKind.External], "Internal preferred."),
            ConnectionMode.PreferExternal =>
                ([RouteKind.External, RouteKind.Internal], "External preferred."),
            _ => Automatic(config, trust)
        };

    private static (IReadOnlyList<RouteKind> Routes, string Reason) Automatic(
        ServerConfig config, NetworkTrust trust) =>
        trust switch
        {
            NetworkTrust.Offline =>
                ([], "No network."),

            NetworkTrust.Trusted =>
                ([RouteKind.Internal, RouteKind.External],
                    "A connected network matches the internal-route rules."),

            // Deliberately no internal probe: an untrusted network must not see
            // the internal hostname just because the app felt like checking.
            NetworkTrust.Untrusted =>
                ([RouteKind.External],
                    "No connected network matches the internal-route rules."),

            _ when config.TrustedNetworks.ProbeInternalOnUnknownNetworks =>
                ([RouteKind.External, RouteKind.Internal],
                    "Network could not be identified; internal address tried only as a fallback."),

            _ => ([RouteKind.External], "Network could not be identified.")
        };
}
