using WindowsCompanion.Core.Models;

namespace WindowsCompanion.Core.Tests;

public class TrustedNetworkCidrTests
{
    [Fact]
    public void Valid_ipv4_and_ipv6_blocks_are_canonicalized()
    {
        var result = TrustedNetworkCidr.Validate(
            ["192.168.50.0/24", "FD12:3456:0000::/48"]);

        Assert.True(result.IsValid);
        Assert.Equal(["192.168.50.0/24", "fd12:3456::/48"], result.CanonicalCidrs);
    }

    [Theory]
    [InlineData("192.168.50.20/24", "192.168.50.0/24")]
    [InlineData("fd12:3456::20/48", "fd12:3456::/48")]
    public void Host_bits_are_rejected_with_the_canonical_network_address(
        string entry,
        string expected)
    {
        var result = TrustedNetworkCidr.Validate([entry]);

        Assert.False(result.IsValid);
        Assert.Contains(expected, result.Errors[0].Message);
    }

    [Theory]
    [InlineData("192.168.1.0", "address/prefix")]
    [InlineData("192.168.1.0/33", "0 to 32")]
    [InlineData("fd12::/129", "0 to 128")]
    [InlineData("fd12::%4/64", "zone IDs")]
    [InlineData("not-an-address/24", "valid IPv4 or IPv6")]
    [InlineData("192.168.001.000/24", "dotted-decimal")]
    [InlineData("0xC0.0xA8.0x01.0x00/24", "dotted-decimal")]
    [InlineData("::ffff:192.168.1.0/120", "corresponding IPv4 CIDR")]
    public void Invalid_entries_have_actionable_errors(string entry, string expected)
    {
        var result = TrustedNetworkCidr.Validate([entry]);

        Assert.False(result.IsValid);
        Assert.Contains(expected, result.Errors[0].Message);
        Assert.Equal(1, result.Errors[0].EntryNumber);
    }

    [Fact]
    public void Duplicate_blocks_are_rejected_after_canonicalization()
    {
        var result = TrustedNetworkCidr.Validate(
            ["fd12:3456::/48", "FD12:3456:0000::/48"]);

        Assert.False(result.IsValid);
        Assert.Contains("Duplicates entry 1", result.Errors[0].Message);
        Assert.Equal(2, result.Errors[0].EntryNumber);
    }

    [Fact]
    public void Overlapping_blocks_are_rejected_but_adjacent_blocks_are_allowed()
    {
        var overlapping = TrustedNetworkCidr.Validate(
            ["192.168.0.0/16", "192.168.50.0/24"]);
        var adjacent = TrustedNetworkCidr.Validate(
            ["192.168.50.0/24", "192.168.51.0/24"]);

        Assert.False(overlapping.IsValid);
        Assert.Contains("Overlaps entry 1", overlapping.Errors[0].Message);
        Assert.True(adjacent.IsValid);
    }

    [Fact]
    public void IPv4_and_ipv6_blocks_never_overlap_each_other()
    {
        var result = TrustedNetworkCidr.Validate(["0.0.0.0/0", "::/0"]);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Any_connected_ipv4_or_ipv6_address_can_match()
    {
        var cidrs = new[] { "10.20.0.0/16", "fd12:3456::/48" };

        Assert.True(TrustedNetworkCidr.Matches(
            cidrs,
            ["192.168.1.20", "fd12:3456::99%14"]));
        Assert.True(TrustedNetworkCidr.Matches(
            cidrs,
            ["10.20.8.4", "2001:db8::1"]));
        Assert.False(TrustedNetworkCidr.Matches(
            cidrs,
            ["192.168.1.20", "2001:db8::1"]));
    }

    [Fact]
    public void Invalid_persisted_entries_fail_closed_instead_of_matching()
    {
        Assert.False(TrustedNetworkCidr.Matches(
            ["192.168.1.0/24", "invalid"],
            ["192.168.1.20"]));
    }

    [Fact]
    public void Overlapping_persisted_entries_fail_closed_instead_of_expanding_trust()
    {
        Assert.False(TrustedNetworkCidr.Matches(
            ["10.0.0.0/16", "10.0.0.0/8"],
            ["10.1.2.3"]));
    }
}
