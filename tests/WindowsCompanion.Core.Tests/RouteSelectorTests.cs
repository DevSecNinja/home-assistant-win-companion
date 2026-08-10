using WindowsCompanion.Core.App;
using WindowsCompanion.Core.Models;
using Xunit;

namespace WindowsCompanion.Core.Tests;

public class RouteSelectorTests
{
    private const string Internal = "http://homeassistant.local:8123/";
    private const string External = "https://ha.example.com/";

    private static ServerConfig Config(
        ConnectionMode mode = ConnectionMode.Automatic,
        string? internalUrl = Internal,
        string? externalUrl = External,
        Action<TrustedNetworkSettings>? trust = null)
    {
        var config = new ServerConfig
        {
            BaseUrl = externalUrl ?? internalUrl ?? string.Empty,
            InternalUrl = internalUrl,
            ExternalUrl = externalUrl,
            ConnectionMode = mode
        };
        config.TrustedNetworks.Ssids.Add("HomeNet");
        trust?.Invoke(config.TrustedNetworks);
        return config;
    }

    private static NetworkContext Home => new(NetworkKind.Wireless, "HomeNet", "aa:bb:cc:dd:ee:ff");
    private static NetworkContext Cafe => new(NetworkKind.Wireless, "CafeGuest", "11:22:33:44:55:66");
    private static NetworkContext NamelessWifi => new(NetworkKind.Wireless, WirelessIdentityUnavailable: true);

    [Fact]
    public void Trusted_wifi_prefers_the_internal_address_with_external_as_a_fallback()
    {
        var plan = RouteSelector.Plan(Config(), Home);

        Assert.Equal(NetworkTrust.Trusted, plan.Trust);
        Assert.Equal([RouteKind.Internal, RouteKind.External], plan.Candidates);
    }

    [Fact]
    public void Untrusted_wifi_never_probes_the_internal_address()
    {
        var plan = RouteSelector.Plan(Config(), Cafe);

        Assert.Equal(NetworkTrust.Untrusted, plan.Trust);
        Assert.Equal([RouteKind.External], plan.Candidates);
        Assert.DoesNotContain(RouteKind.Internal, plan.Candidates);
    }

    [Fact]
    public void Wifi_whose_name_windows_withholds_is_unidentifiable_not_untrusted()
    {
        var plan = RouteSelector.Plan(Config(), NamelessWifi);

        Assert.Equal(NetworkTrust.Unidentifiable, plan.Trust);
        Assert.Equal([RouteKind.External], plan.Candidates);
    }

    [Fact]
    public void Unidentifiable_network_may_fall_back_to_internal_only_when_opted_in()
    {
        var config = Config(trust: t => t.ProbeInternalOnUnknownNetworks = true);

        var plan = RouteSelector.Plan(config, NamelessWifi);

        Assert.Equal([RouteKind.External, RouteKind.Internal], plan.Candidates);
    }

    [Fact]
    public void Vpn_makes_an_unrecognized_network_unidentifiable_rather_than_untrusted()
    {
        // A tunnel can carry the internal address, so an unrecognized network under
        // a VPN is not evidence of being away from home.
        var plan = RouteSelector.Plan(Config(), Cafe with { VpnActive = true });

        Assert.Equal(NetworkTrust.Unidentifiable, plan.Trust);
        Assert.Equal([RouteKind.External], plan.Candidates);
    }

    [Fact]
    public void A_vpn_does_not_untrust_a_network_the_user_recognizes()
    {
        // The SSID still proves the machine is on the home LAN; if the tunnel
        // happens to break the internal address, the probe falls back to external.
        var plan = RouteSelector.Plan(Config(), Home with { VpnActive = true });

        Assert.Equal(NetworkTrust.Trusted, plan.Trust);
        Assert.Equal([RouteKind.Internal, RouteKind.External], plan.Candidates);
    }

    [Fact]
    public void Wired_network_is_untrusted_until_the_user_opts_in()
    {
        var wired = new NetworkContext(NetworkKind.Wired);

        Assert.Equal(NetworkTrust.Untrusted, RouteSelector.Plan(Config(), wired).Trust);
        Assert.Equal(
            NetworkTrust.Trusted,
            RouteSelector.Plan(Config(trust: t => t.TrustWiredNetworks = true), wired).Trust);
    }

    [Fact]
    public void Without_any_trusted_network_every_network_is_unidentifiable()
    {
        var config = Config();
        config.TrustedNetworks = new TrustedNetworkSettings();

        Assert.Equal(NetworkTrust.Unidentifiable, RouteSelector.Plan(config, Home).Trust);
    }

    [Fact]
    public void A_cidr_matching_any_active_interface_prefers_internal()
    {
        var config = Config(trust: settings =>
        {
            settings.Ssids.Clear();
            settings.Cidrs.Add("10.50.0.0/16");
        });
        var network = new NetworkContext(
            NetworkKind.Wireless,
            "CafeGuest",
            LocalAddresses: ["192.168.1.20", "10.50.8.4"]);

        var plan = RouteSelector.Plan(config, network);

        Assert.Equal(NetworkTrust.Trusted, plan.Trust);
        Assert.Equal([RouteKind.Internal, RouteKind.External], plan.Candidates);
    }

    [Fact]
    public void A_definite_cidr_nonmatch_uses_external_even_when_wifi_name_is_unavailable()
    {
        var config = Config(trust: settings =>
        {
            settings.Ssids.Clear();
            settings.Cidrs.Add("192.168.50.0/24");
        });
        var network = new NetworkContext(
            NetworkKind.Wireless,
            WirelessIdentityUnavailable: true,
            LocalAddresses: ["192.168.51.20"]);

        var plan = RouteSelector.Plan(config, network);

        Assert.Equal(NetworkTrust.Untrusted, plan.Trust);
        Assert.Equal([RouteKind.External], plan.Candidates);
    }

    [Fact]
    public void Captured_addresses_do_not_change_ssid_only_unknown_network_fallback()
    {
        var config = Config(trust: settings =>
            settings.ProbeInternalOnUnknownNetworks = true);
        var network = new NetworkContext(
            NetworkKind.Wireless,
            WirelessIdentityUnavailable: true,
            LocalAddresses: ["192.168.51.20"]);

        var plan = RouteSelector.Plan(config, network);

        Assert.Equal(NetworkTrust.Unidentifiable, plan.Trust);
        Assert.Equal([RouteKind.External, RouteKind.Internal], plan.Candidates);
    }

    [Fact]
    public void A_cidr_match_remains_trusted_while_a_vpn_is_active()
    {
        var config = Config(trust: settings =>
        {
            settings.Ssids.Clear();
            settings.Cidrs.Add("fd12:3456::/48");
        });
        var network = new NetworkContext(
            NetworkKind.Wired,
            VpnActive: true,
            LocalAddresses: ["fd12:3456::20"]);

        Assert.Equal(NetworkTrust.Trusted, RouteSelector.Plan(config, network).Trust);
    }

    [Fact]
    public void Offline_yields_no_candidates_at_all()
    {
        var plan = RouteSelector.Plan(Config(), NetworkContext.Offline);

        Assert.Equal(NetworkTrust.Offline, plan.Trust);
        Assert.Empty(plan.Candidates);
    }

    [Fact]
    public void Bssid_matching_is_only_enforced_when_the_user_asks_for_it()
    {
        var config = Config(trust: t =>
        {
            t.RequireBssidMatch = true;
            t.Bssids.Add("AA:BB:CC:DD:EE:FF");
        });

        Assert.Equal(NetworkTrust.Trusted, RouteSelector.Plan(config, Home).Trust);
        Assert.Equal(
            NetworkTrust.Untrusted,
            RouteSelector.Plan(config, Home with { Bssid = "99:88:77:66:55:44" }).Trust);
    }

    [Theory]
    [InlineData(ConnectionMode.InternalOnly, RouteKind.Internal)]
    [InlineData(ConnectionMode.ExternalOnly, RouteKind.External)]
    public void Only_modes_ignore_the_network_entirely(ConnectionMode mode, RouteKind expected)
    {
        foreach (var network in new[] { Home, Cafe, NamelessWifi })
            Assert.Equal([expected], RouteSelector.Plan(Config(mode), network).Candidates);
    }

    [Theory]
    [InlineData(ConnectionMode.PreferInternal, RouteKind.Internal, RouteKind.External)]
    [InlineData(ConnectionMode.PreferExternal, RouteKind.External, RouteKind.Internal)]
    public void Prefer_modes_fix_the_order_regardless_of_network(
        ConnectionMode mode, RouteKind first, RouteKind second)
    {
        foreach (var network in new[] { Home, Cafe })
            Assert.Equal([first, second], RouteSelector.Plan(Config(mode), network).Candidates);
    }

    [Fact]
    public void A_single_configured_address_is_used_even_on_an_untrusted_network()
    {
        var plan = RouteSelector.Plan(Config(externalUrl: null), Cafe);

        Assert.Equal([RouteKind.Internal], plan.Candidates);
        Assert.Contains("Only one address", plan.Reason);
    }

    [Fact]
    public void Internal_only_without_an_internal_address_still_yields_nothing_usable()
    {
        var plan = RouteSelector.Plan(Config(ConnectionMode.InternalOnly, internalUrl: null), Home);

        // The external address is all there is, and the validator refuses to save
        // this combination in the first place.
        Assert.Equal([RouteKind.External], plan.Candidates);
    }

    [Fact]
    public void No_configured_address_yields_no_candidates()
    {
        var plan = RouteSelector.Plan(Config(internalUrl: null, externalUrl: null), Home);

        Assert.Empty(plan.Candidates);
    }

    [Fact]
    public void Offline_with_a_single_address_still_refuses_to_connect()
    {
        var plan = RouteSelector.Plan(Config(externalUrl: null), NetworkContext.Offline);

        Assert.Empty(plan.Candidates);
    }
}
