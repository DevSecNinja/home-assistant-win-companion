using WindowsCompanion.Core.Models;
using Xunit;

namespace WindowsCompanion.Core.Tests;

public class TrustedNetworkSettingsTests
{
    private static TrustedNetworkSettings Home() => new() { Ssids = { "HomeNet" } };

    [Fact]
    public void An_empty_configuration_trusts_nothing()
    {
        var settings = new TrustedNetworkSettings();

        Assert.False(settings.IsConfigured);
        Assert.False(settings.Trusts(new NetworkContext(NetworkKind.Wireless, "HomeNet")));
        Assert.False(settings.Trusts(new NetworkContext(NetworkKind.Wired)));
    }

    [Fact]
    public void A_matching_address_in_any_configured_cidr_is_trusted()
    {
        var settings = new TrustedNetworkSettings
        {
            Cidrs = ["192.168.50.0/24", "fd12:3456::/48"]
        };
        var network = new NetworkContext(
            NetworkKind.Wireless,
            WirelessIdentityUnavailable: true,
            LocalAddresses: ["10.0.0.20", "fd12:3456::20"]);

        Assert.True(settings.IsConfigured);
        Assert.True(settings.Trusts(network));
    }

    [Fact]
    public void Nonmatching_addresses_do_not_trust_the_network()
    {
        var settings = new TrustedNetworkSettings { Cidrs = ["192.168.50.0/24"] };
        var network = new NetworkContext(
            NetworkKind.Wired,
            LocalAddresses: ["192.168.51.20", "2001:db8::20"]);

        Assert.False(settings.Trusts(network));
    }

    [Fact]
    public void A_validated_copy_is_detached_and_uses_canonical_cidrs()
    {
        var settings = new TrustedNetworkSettings
        {
            Cidrs = ["FD12:3456:0000::/48"],
            Ssids = ["HomeNet"]
        };

        var copy = settings.ValidatedCopy();
        settings.Cidrs.Clear();
        settings.Ssids.Clear();

        Assert.Equal(["fd12:3456::/48"], copy.Cidrs);
        Assert.Equal(["HomeNet"], copy.Ssids);
    }

    [Fact]
    public void A_matching_ssid_is_trusted()
    {
        Assert.True(Home().Trusts(new NetworkContext(NetworkKind.Wireless, "HomeNet")));
    }

    [Fact]
    public void Ssid_matching_is_case_sensitive_because_wifi_names_are()
    {
        Assert.False(Home().Trusts(new NetworkContext(NetworkKind.Wireless, "homenet")));
    }

    [Fact]
    public void A_wifi_network_windows_will_not_name_is_never_trusted()
    {
        var network = new NetworkContext(NetworkKind.Wireless, WirelessIdentityUnavailable: true);

        Assert.False(Home().Trusts(network));
        Assert.False(network.IsIdentifiable);
    }

    [Fact]
    public void Bssids_are_ignored_unless_matching_is_required()
    {
        var settings = Home();
        settings.Bssids.Add("AA:BB:CC:DD:EE:FF");

        Assert.True(settings.Trusts(new NetworkContext(NetworkKind.Wireless, "HomeNet", "00:00:00:00:00:00")));
    }

    [Fact]
    public void Required_bssid_matching_is_case_insensitive_and_rejects_other_access_points()
    {
        var settings = Home();
        settings.RequireBssidMatch = true;
        settings.Bssids.Add("AA:BB:CC:DD:EE:FF");

        Assert.True(settings.Trusts(new NetworkContext(NetworkKind.Wireless, "HomeNet", "aa:bb:cc:dd:ee:ff")));
        Assert.False(settings.Trusts(new NetworkContext(NetworkKind.Wireless, "HomeNet", "11:22:33:44:55:66")));
        Assert.False(settings.Trusts(new NetworkContext(NetworkKind.Wireless, "HomeNet")));
    }

    [Fact]
    public void Requiring_a_bssid_without_recording_any_falls_back_to_the_ssid()
    {
        var settings = Home();
        settings.RequireBssidMatch = true;

        Assert.True(settings.Trusts(new NetworkContext(NetworkKind.Wireless, "HomeNet")));
    }

    [Fact]
    public void Wired_networks_are_only_trusted_when_the_user_opts_in()
    {
        var settings = Home();

        Assert.False(settings.Trusts(new NetworkContext(NetworkKind.Wired)));

        settings.TrustWiredNetworks = true;
        Assert.True(settings.Trusts(new NetworkContext(NetworkKind.Wired)));
        Assert.True(settings.IsConfigured);
    }

    [Fact]
    public void Offline_and_unknown_networks_are_never_trusted()
    {
        var settings = Home();
        settings.TrustWiredNetworks = true;

        Assert.False(settings.Trusts(NetworkContext.Offline));
        Assert.False(settings.Trusts(new NetworkContext(NetworkKind.Unknown)));
    }
}
