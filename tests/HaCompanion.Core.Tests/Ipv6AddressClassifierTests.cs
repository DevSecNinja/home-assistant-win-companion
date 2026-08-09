using HaCompanion.Core.Sensors;

namespace HaCompanion.Core.Tests;

public class Ipv6AddressClassifierTests
{
    [Theory]
    [InlineData("2001:db8::1234", Ipv6Scope.Global)]
    [InlineData("2a02:a45f:1:2::7", Ipv6Scope.Global)]
    [InlineData("fd12:3456:789a::1", Ipv6Scope.UniqueLocal)]
    [InlineData("fc00::1", Ipv6Scope.UniqueLocal)]
    [InlineData("fe80::1c2f:8a11:ffee:1", Ipv6Scope.LinkLocal)]
    [InlineData("fe80::1%12", Ipv6Scope.LinkLocal)]
    [InlineData("::1", Ipv6Scope.Loopback)]
    [InlineData("::", Ipv6Scope.Unspecified)]
    [InlineData("ff02::1", Ipv6Scope.Multicast)]
    [InlineData("2002:c058:6301::1", Ipv6Scope.Tunnel)]
    [InlineData("2001:0:4137:9e76::1", Ipv6Scope.Tunnel)]
    [InlineData("::ffff:192.168.1.10", Ipv6Scope.Ipv4Mapped)]
    [InlineData("192.168.1.10", Ipv6Scope.Invalid)]
    [InlineData("not-an-address", Ipv6Scope.Invalid)]
    [InlineData("", Ipv6Scope.Invalid)]
    [InlineData(null, Ipv6Scope.Invalid)]
    public void Classifies_addresses_by_scope(string? address, Ipv6Scope expected) =>
        Assert.Equal(expected, Ipv6AddressClassifier.Classify(address));

    [Fact]
    public void Prefers_a_global_address_over_everything_else()
    {
        var selected = Ipv6AddressClassifier.SelectPreferred(
        [
            new("fe80::1%7"),
            new("fd00::5"),
            new("2001:db8::1234"),
            new("::1")
        ]);

        Assert.Equal("2001:db8::1234", selected);
    }

    [Fact]
    public void Prefers_a_stable_global_address_over_a_temporary_one()
    {
        var selected = Ipv6AddressClassifier.SelectPreferred(
        [
            new("2001:db8::dead:beef", Origin: Ipv6AddressOrigin.Temporary),
            new("2001:db8::1", Origin: Ipv6AddressOrigin.Stable)
        ]);

        Assert.Equal("2001:db8::1", selected);
    }

    [Fact]
    public void Falls_back_to_a_temporary_global_address_when_no_stable_one_exists()
    {
        var selected = Ipv6AddressClassifier.SelectPreferred(
        [
            new("fd00::7", Origin: Ipv6AddressOrigin.Stable),
            new("2001:db8::dead:beef", Origin: Ipv6AddressOrigin.Temporary)
        ]);

        Assert.Equal("2001:db8::dead:beef", selected);
    }

    [Fact]
    public void Reports_a_unique_local_address_when_no_global_address_exists()
    {
        var selected = Ipv6AddressClassifier.SelectPreferred(
        [
            new("fe80::abcd%9"),
            new("fd12:3456:789a::1")
        ]);

        Assert.Equal("fd12:3456:789a::1", selected);
    }

    [Fact]
    public void Ignores_deprecated_and_duplicate_detection_failures()
    {
        var selected = Ipv6AddressClassifier.SelectPreferred(
        [
            new("2001:db8::dead", State: Ipv6AddressState.Deprecated),
            new("2001:db8::beef", State: Ipv6AddressState.Invalid),
            new("fd00::1")
        ]);

        Assert.Equal("fd00::1", selected);
    }

    [Fact]
    public void Ignores_tunnel_link_local_and_loopback_only_adapters()
    {
        var selected = Ipv6AddressClassifier.SelectPreferred(
        [
            new("fe80::1%3"),
            new("2002:c058:6301::1"),
            new("2001:0:4137:9e76:2c3a:2b6f:3f57:fefa"),
            new("::1")
        ]);

        Assert.Null(selected);
    }

    [Fact]
    public void Selects_deterministically_when_several_addresses_rank_equally()
    {
        var forward = Ipv6AddressClassifier.SelectPreferred(
            [new("2001:db8::20"), new("2001:db8::10")]);
        var reversed = Ipv6AddressClassifier.SelectPreferred(
            [new("2001:db8::10"), new("2001:db8::20")]);

        Assert.Equal(forward, reversed);
        Assert.Equal("2001:db8::10", forward);
    }

    [Fact]
    public void Strips_the_zone_index_from_the_reported_value()
    {
        var selected = Ipv6AddressClassifier.SelectPreferred([new("2001:db8::1%14")]);
        Assert.Equal("2001:db8::1", selected);
    }

    [Fact]
    public void Returns_null_for_an_adapter_with_no_addresses()
    {
        Assert.Null(Ipv6AddressClassifier.SelectPreferred(null));
        Assert.Null(Ipv6AddressClassifier.SelectPreferred([]));
    }
}
