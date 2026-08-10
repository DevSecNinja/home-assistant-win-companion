using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace WindowsCompanion.Core.Models;

/// <summary>A user-facing validation problem for one trusted-network CIDR entry.</summary>
public sealed record TrustedNetworkCidrError(int EntryNumber, string Entry, string Message);

/// <summary>Canonical CIDRs and any problems found while validating a user-managed list.</summary>
public sealed record TrustedNetworkCidrValidation(
    IReadOnlyList<string> CanonicalCidrs,
    IReadOnlyList<TrustedNetworkCidrError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Parses and matches IPv4 and IPv6 CIDR blocks used to decide whether the
/// internal Home Assistant address is eligible.
/// </summary>
public static class TrustedNetworkCidr
{
    private sealed record ParsedCidr(byte[] Network, int PrefixLength, string Canonical);

    public static TrustedNetworkCidrValidation Validate(IEnumerable<string>? entries)
    {
        var canonical = new List<string>();
        var errors = new List<TrustedNetworkCidrError>();
        var parsed = new List<(int EntryNumber, ParsedCidr Cidr)>();
        var entryNumber = 0;

        foreach (var raw in entries ?? [])
        {
            entryNumber++;
            var entry = raw?.Trim() ?? string.Empty;
            if (entry.Length == 0) continue;

            if (!TryParse(entry, out var cidr, out var problem))
            {
                errors.Add(new TrustedNetworkCidrError(entryNumber, entry, problem));
                continue;
            }

            var overlap = parsed.FirstOrDefault(existing => Overlaps(existing.Cidr, cidr));
            if (overlap.Cidr is not null)
            {
                var duplicate = string.Equals(
                    overlap.Cidr.Canonical,
                    cidr.Canonical,
                    StringComparison.Ordinal);
                errors.Add(new TrustedNetworkCidrError(
                    entryNumber,
                    entry,
                    duplicate
                        ? $"Duplicates entry {overlap.EntryNumber} ({overlap.Cidr.Canonical})."
                        : $"Overlaps entry {overlap.EntryNumber} ({overlap.Cidr.Canonical}); keep only the more appropriate block."));
                continue;
            }

            parsed.Add((entryNumber, cidr));
            canonical.Add(cidr.Canonical);
        }

        return new TrustedNetworkCidrValidation(canonical, errors);
    }

    /// <summary>True when any connected address belongs to any valid configured block.</summary>
    public static bool Matches(IEnumerable<string>? cidrs, IEnumerable<string>? addresses)
    {
        if (cidrs is null || addresses is null) return false;

        var validation = Validate(cidrs);
        if (!validation.IsValid) return false;

        var parsedCidrs = validation.CanonicalCidrs
            .Select(entry => TryParse(entry, out var cidr, out _) ? cidr : null)
            .Where(cidr => cidr is not null)
            .ToList();
        if (parsedCidrs.Count == 0) return false;

        foreach (var text in addresses)
        {
            if (!TryParseAddress(text, out var address)) continue;
            var bytes = address.GetAddressBytes();
            if (parsedCidrs.Any(cidr => cidr!.Network.Length == bytes.Length && Contains(cidr, bytes)))
                return true;
        }

        return false;
    }

    private static bool TryParse(
        string? entry,
        out ParsedCidr cidr,
        out string problem)
    {
        cidr = null!;
        problem = string.Empty;

        if (string.IsNullOrWhiteSpace(entry))
        {
            problem = "Enter a block such as 192.168.1.0/24 or fd12:3456:789a::/48.";
            return false;
        }

        var separator = entry.IndexOf('/');
        if (separator <= 0 || separator != entry.LastIndexOf('/') || separator == entry.Length - 1)
        {
            problem = "Use address/prefix notation, such as 192.168.1.0/24 or fd12:3456:789a::/48.";
            return false;
        }

        var addressText = entry[..separator];
        var prefixText = entry[(separator + 1)..];
        if (addressText.Contains('%', StringComparison.Ordinal)
            || !IPAddress.TryParse(addressText, out var address)
            || address.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
        {
            problem = "The address is not a valid IPv4 or IPv6 address. IPv6 zone IDs are not allowed.";
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork
            && !string.Equals(addressText, address.ToString(), StringComparison.Ordinal))
        {
            problem = $"Use canonical dotted-decimal IPv4 notation: {address}.";
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            problem = $"Use the corresponding IPv4 CIDR for {address.MapToIPv4()}.";
            return false;
        }

        var maximumPrefix = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        if (!int.TryParse(prefixText, NumberStyles.None, CultureInfo.InvariantCulture, out var prefix)
            || prefix < 0
            || prefix > maximumPrefix)
        {
            problem = $"The prefix must be a whole number from 0 to {maximumPrefix}.";
            return false;
        }

        var bytes = address.GetAddressBytes();
        var network = ApplyMask(bytes, prefix);
        if (!bytes.SequenceEqual(network))
        {
            var canonicalNetwork = new IPAddress(network);
            problem = $"Use the network address {canonicalNetwork}/{prefix}; host bits must be zero.";
            return false;
        }

        cidr = new ParsedCidr(network, prefix, $"{new IPAddress(network)}/{prefix}");
        return true;
    }

    private static bool TryParseAddress(string? text, out IPAddress address)
    {
        address = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var zone = text.IndexOf('%');
        var bare = zone >= 0 ? text[..zone] : text;
        if (!IPAddress.TryParse(bare, out var parsed)
            || parsed.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
        {
            return false;
        }

        address = parsed;
        return true;
    }

    private static byte[] ApplyMask(byte[] address, int prefixLength)
    {
        var network = address.ToArray();
        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        if (remainingBits > 0)
        {
            network[fullBytes] &= (byte)(0xFF << (8 - remainingBits));
            fullBytes++;
        }

        Array.Clear(network, fullBytes, network.Length - fullBytes);
        return network;
    }

    private static bool Overlaps(ParsedCidr left, ParsedCidr right) =>
        left.Network.Length == right.Network.Length
        && (Contains(left, right.Network) || Contains(right, left.Network));

    private static bool Contains(ParsedCidr cidr, byte[] address)
    {
        var fullBytes = cidr.PrefixLength / 8;
        for (var i = 0; i < fullBytes; i++)
        {
            if (cidr.Network[i] != address[i]) return false;
        }

        var remainingBits = cidr.PrefixLength % 8;
        if (remainingBits == 0) return true;

        var mask = (byte)(0xFF << (8 - remainingBits));
        return (cidr.Network[fullBytes] & mask) == (address[fullBytes] & mask);
    }
}
