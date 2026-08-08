using System.Net;
using System.Net.Sockets;

namespace HaCompanion.Core.Sensors;

/// <summary>What an IPv6 address is for, decided from the address itself.</summary>
public enum Ipv6Scope
{
    /// <summary>Globally routable unicast (2000::/3).</summary>
    Global,

    /// <summary>Unique local address, fc00::/7. Routable inside the site only.</summary>
    UniqueLocal,

    /// <summary>fe80::/10. Interface-scoped and meaningless outside this machine's link.</summary>
    LinkLocal,

    /// <summary>::1.</summary>
    Loopback,

    /// <summary>ff00::/8.</summary>
    Multicast,

    /// <summary>::</summary>
    Unspecified,

    /// <summary>6to4 (2002::/16) or Teredo (2001:0::/32): present without real IPv6 connectivity.</summary>
    Tunnel,

    /// <summary>IPv4-mapped or IPv4-compatible: not an IPv6 identity of its own.</summary>
    Ipv4Mapped,

    /// <summary>Not a parseable IPv6 address, or reserved space we deliberately ignore.</summary>
    Invalid
}

/// <summary>
/// Classifies IPv6 addresses and picks the one worth reporting. Pure, so the
/// preference rules can be tested without a network stack.
/// </summary>
public static class Ipv6AddressClassifier
{
    public static Ipv6Scope Classify(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return Ipv6Scope.Invalid;

        // Windows appends a zone index to link-local addresses ("fe80::1%12").
        var zoneSeparator = address.IndexOf('%');
        var bare = zoneSeparator >= 0 ? address[..zoneSeparator] : address;

        if (!IPAddress.TryParse(bare, out var parsed)
            || parsed.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return Ipv6Scope.Invalid;
        }

        var bytes = parsed.GetAddressBytes();

        if (parsed.Equals(IPAddress.IPv6Loopback)) return Ipv6Scope.Loopback;
        if (parsed.Equals(IPAddress.IPv6Any)) return Ipv6Scope.Unspecified;
        if (bytes[0] == 0xFF) return Ipv6Scope.Multicast;
        if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80) return Ipv6Scope.LinkLocal;
        if ((bytes[0] & 0xFE) == 0xFC) return Ipv6Scope.UniqueLocal;

        // ::ffff:a.b.c.d and the deprecated ::a.b.c.d form.
        if (bytes.Take(10).All(b => b == 0)) return Ipv6Scope.Ipv4Mapped;

        if (bytes[0] == 0x20 && bytes[1] == 0x02) return Ipv6Scope.Tunnel;                    // 6to4
        if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x00 && bytes[3] == 0x00)
            return Ipv6Scope.Tunnel;                                                          // Teredo

        return (bytes[0] & 0xE0) == 0x20 ? Ipv6Scope.Global : Ipv6Scope.Invalid;
    }

    /// <summary>
    /// Picks the address to report from everything an adapter holds.
    /// </summary>
    /// <remarks>
    /// A globally routable address wins; a unique local address is reported when no
    /// global one exists, because on an IPv6 LAN without a routed prefix it is the
    /// address that actually identifies this PC to Home Assistant. Stable addresses
    /// beat RFC 4941 temporary ones so the entity does not churn on every rotation.
    /// Link-local, loopback, multicast, unspecified, tunnel and IPv4-mapped
    /// addresses are never reported, and neither are deprecated or
    /// duplicate-address-detection failures.
    /// </remarks>
    public static string? SelectPreferred(IEnumerable<Ipv6AddressInfo>? addresses)
    {
        if (addresses is null) return null;

        return addresses
            .Where(a => a.State == Ipv6AddressState.Preferred)
            .Select(a => (Address: a, Rank: RankOf(a)))
            .Where(candidate => candidate.Rank < int.MaxValue)
            .OrderBy(candidate => candidate.Rank)
            .ThenBy(candidate => candidate.Address.Address, StringComparer.Ordinal)
            .Select(candidate => Normalize(candidate.Address.Address))
            .FirstOrDefault();
    }

    private static int RankOf(Ipv6AddressInfo address)
    {
        var scopeRank = Classify(address.Address) switch
        {
            Ipv6Scope.Global => 0,
            Ipv6Scope.UniqueLocal => 2,
            _ => int.MaxValue
        };

        if (scopeRank == int.MaxValue) return int.MaxValue;
        return scopeRank + (address.Origin == Ipv6AddressOrigin.Temporary ? 1 : 0);
    }

    /// <summary>Lower-case, zone-free canonical text, which is what Home Assistant expects.</summary>
    private static string Normalize(string address)
    {
        var zoneSeparator = address.IndexOf('%');
        var bare = zoneSeparator >= 0 ? address[..zoneSeparator] : address;
        return IPAddress.TryParse(bare, out var parsed) ? parsed.ToString() : bare;
    }
}
