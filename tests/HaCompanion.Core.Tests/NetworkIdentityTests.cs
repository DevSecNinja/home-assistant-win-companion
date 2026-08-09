using HaCompanion.Core.Sensors;

namespace HaCompanion.Core.Tests;

public class NetworkIdentityTests
{
    private static NetworkAdapterSnapshot Ethernet(
        string id = "eth",
        bool isUp = true,
        bool hasGateway = true,
        string[]? ipv4 = null,
        Ipv6AddressInfo[]? ipv6 = null,
        byte[]? mac = null,
        bool isVirtual = false,
        string description = "Intel Ethernet Connection") =>
        new(id, description, NetworkAdapterKind.Wired, isUp, isVirtual, hasGateway,
            ipv4 ?? ["192.168.1.20"], ipv6 ?? [], mac ?? [0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF]);

    private static NetworkAdapterSnapshot WiFi(
        string id = "wlan",
        bool isUp = true,
        bool hasGateway = true,
        string[]? ipv4 = null,
        Ipv6AddressInfo[]? ipv6 = null,
        byte[]? mac = null) =>
        new(id, "Intel Wi-Fi 6 AX201", NetworkAdapterKind.Wireless, isUp, false, hasGateway,
            ipv4 ?? ["192.168.1.30"], ipv6 ?? [], mac ?? [0x11, 0x22, 0x33, 0x44, 0x55, 0x66]);

    private static NetworkAdapterSnapshot Vpn(
        string id = "vpn",
        string[]? ipv4 = null,
        Ipv6AddressInfo[]? ipv6 = null) =>
        new(id, "WireGuard Tunnel", NetworkAdapterKind.Tunnel, true, true, true,
            ipv4 ?? ["10.8.0.2"], ipv6 ?? [], null);

    private static NetworkAdapterSnapshot HyperV(string id = "vswitch") =>
        new(id, "Hyper-V Virtual Ethernet Adapter", NetworkAdapterKind.Wired, true, true, false,
            ["172.28.0.1"], [], [0x00, 0x15, 0x5D, 0x01, 0x02, 0x03]);

    private static NetworkAdapterSnapshot Loopback() =>
        new("lo", "Software Loopback Interface 1", NetworkAdapterKind.Loopback, true, true, false,
            ["127.0.0.1"], [new("::1")], null);

    [Theory]
    [InlineData(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF }, "AA:BB:CC:DD:EE:FF")]
    [InlineData(new byte[] { 0x00, 0x15, 0x5D, 0x01, 0x02, 0x03 }, "00:15:5D:01:02:03")]
    public void Formats_mac_addresses_as_uppercase_colon_separated_bytes(byte[] bytes, string expected) =>
        Assert.Equal(expected, MacAddressFormatter.Format(bytes));

    [Fact]
    public void Rejects_unusable_hardware_addresses()
    {
        Assert.Null(MacAddressFormatter.Format(null));
        Assert.Null(MacAddressFormatter.Format([]));
        Assert.Null(MacAddressFormatter.Format([0x00, 0x00, 0x00, 0x00, 0x00, 0x00]));
        Assert.Null(MacAddressFormatter.Format([0xAA, 0xBB, 0xCC]));
    }

    [Theory]
    [InlineData("Hyper-V Virtual Ethernet Adapter", true)]
    [InlineData("vEthernet (WSL (Hyper-V firewall))", true)]
    [InlineData("VMware Virtual Ethernet Adapter for VMnet8", true)]
    [InlineData("TAP-Windows Adapter V9", true)]
    [InlineData("WireGuard Tunnel", true)]
    [InlineData("Tailscale Tunnel", true)]
    [InlineData("Software Loopback Interface 1", true)]
    [InlineData("Intel(R) Ethernet Connection I219-LM", false)]
    [InlineData("Intel(R) Wi-Fi 6 AX201 160MHz", false)]
    [InlineData("Realtek USB GbE Family Controller", false)]
    [InlineData(null, false)]
    public void Recognises_virtual_adapters_by_description(string? description, bool expected) =>
        Assert.Equal(expected, NetworkAdapterSelector.LooksVirtual(description));

    [Fact]
    public void Selects_the_adapter_carrying_the_active_route()
    {
        var selected = NetworkAdapterSelector.SelectActive(
            [Loopback(), WiFi(), Ethernet()],
            routeLocalIpv4: "192.168.1.20");

        Assert.Equal("eth", selected!.Id);
    }

    [Fact]
    public void Matches_the_route_on_ipv6_when_ipv4_is_unavailable()
    {
        var selected = NetworkAdapterSelector.SelectActive(
            [WiFi(), Ethernet(ipv6: [new("2001:db8::20")])],
            routeLocalIpv6: "2001:DB8::20");

        Assert.Equal("eth", selected!.Id);
    }

    [Fact]
    public void Keeps_the_physical_adapter_when_the_route_runs_through_a_vpn()
    {
        var selected = NetworkAdapterSelector.SelectActive(
            [Vpn(), WiFi(), HyperV()],
            routeLocalIpv4: "10.8.0.2");

        Assert.Equal("wlan", selected!.Id);
    }

    [Fact]
    public void Ignores_virtual_switches_when_a_physical_adapter_exists()
    {
        var selected = NetworkAdapterSelector.SelectActive([HyperV(), Ethernet()]);
        Assert.Equal("eth", selected!.Id);
    }

    [Fact]
    public void Falls_back_to_the_routed_tunnel_when_no_physical_adapter_is_up()
    {
        var selected = NetworkAdapterSelector.SelectActive(
            [Loopback(), Ethernet(isUp: false), Vpn()],
            routeLocalIpv4: "10.8.0.2");

        Assert.Equal("vpn", selected!.Id);
    }

    [Fact]
    public void Prefers_an_adapter_with_a_default_gateway_when_routing_is_unknown()
    {
        var selected = NetworkAdapterSelector.SelectActive(
            [WiFi(hasGateway: false), Ethernet(hasGateway: true)]);

        Assert.Equal("eth", selected!.Id);
    }

    [Fact]
    public void Returns_nothing_when_only_loopback_is_up()
    {
        Assert.Null(NetworkAdapterSelector.SelectActive([Loopback()]));
        Assert.Null(NetworkAdapterSelector.SelectActive([]));
        Assert.Null(NetworkAdapterSelector.SelectActive(null));
    }

    [Fact]
    public void Derives_every_reading_from_the_same_adapter()
    {
        var identity = NetworkIdentity.From(
            [
                Loopback(),
                HyperV(),
                Vpn(ipv6: [new("2001:db8:99::9")]),
                Ethernet(
                    ipv4: ["192.168.1.20"],
                    ipv6: [new("fe80::1%4"), new("2001:db8::20")],
                    mac: [0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF])
            ],
            routeLocalIpv4: "192.168.1.20",
            routeLocalIpv6: "2001:db8::20");

        Assert.Equal(NetworkClassifier.Ethernet, identity.ConnectionType);
        Assert.Equal("192.168.1.20", identity.Ipv4Address);
        Assert.Equal("2001:db8::20", identity.Ipv6Address);
        Assert.Equal("AA:BB:CC:DD:EE:FF", identity.MacAddress);
    }

    [Fact]
    public void Follows_the_active_adapter_across_a_route_change()
    {
        var adapters = new[]
        {
            WiFi(ipv4: ["192.168.1.30"], ipv6: [new("2001:db8::30")],
                mac: [0x11, 0x22, 0x33, 0x44, 0x55, 0x66]),
            Ethernet(ipv4: ["192.168.1.20"], ipv6: [new("2001:db8::20")])
        };

        var docked = NetworkIdentity.From(adapters, routeLocalIpv4: "192.168.1.20");
        var undocked = NetworkIdentity.From(adapters, routeLocalIpv4: "192.168.1.30");

        Assert.Equal("AA:BB:CC:DD:EE:FF", docked.MacAddress);
        Assert.Equal("2001:db8::20", docked.Ipv6Address);
        Assert.Equal("11:22:33:44:55:66", undocked.MacAddress);
        Assert.Equal("2001:db8::30", undocked.Ipv6Address);
    }

    [Fact]
    public void Reports_not_connected_when_the_adapter_has_no_ipv6_or_hardware_address()
    {
        var identity = NetworkIdentity.From(
            [Loopback(), Vpn(ipv4: ["10.8.0.2"])],
            routeLocalIpv4: "10.8.0.2");

        Assert.Equal("10.8.0.2", identity.Ipv4Address);
        Assert.Equal(NetworkClassifier.NotConnected, identity.Ipv6Address);
        Assert.Equal(NetworkClassifier.NotConnected, identity.MacAddress);
    }

    [Fact]
    public void Reports_not_connected_when_nothing_but_loopback_is_up()
    {
        var identity = NetworkIdentity.From([Loopback(), Ethernet(isUp: false)]);

        Assert.Equal(NetworkClassifier.NotConnected, identity.ConnectionType);
        Assert.Equal(NetworkClassifier.NotConnected, identity.Ipv4Address);
        Assert.Equal(NetworkClassifier.NotConnected, identity.Ipv6Address);
        Assert.Equal(NetworkClassifier.NotConnected, identity.MacAddress);
    }

    [Fact]
    public void Ignores_an_automatic_private_ipv4_address()
    {
        var identity = NetworkIdentity.From([Ethernet(ipv4: ["169.254.10.5"])]);
        Assert.Equal(NetworkClassifier.NotConnected, identity.Ipv4Address);
    }

    [Fact]
    public void Classifies_connection_type_from_the_same_snapshot()
    {
        Assert.Equal(
            NetworkClassifier.WiFi,
            NetworkClassifier.ClassifyAdapters(new[] { Loopback(), Ethernet(isUp: false), WiFi() }));
    }
}
