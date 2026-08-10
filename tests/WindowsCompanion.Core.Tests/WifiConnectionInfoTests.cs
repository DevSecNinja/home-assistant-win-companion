using WindowsCompanion.Core.Sensors;

namespace WindowsCompanion.Core.Tests;

public class WifiConnectionInfoTests
{
    [Fact]
    public void Formats_connected_identifiers()
    {
        var info = new WifiConnectionInfo(
            WifiConnectionStatus.Connected,
            "Home Wi-Fi",
            [0xAA, 0xBB, 0xCC, 0x01, 0x02, 0x03]);

        Assert.Equal("Home Wi-Fi", info.SsidState);
        Assert.Equal("AA:BB:CC:01:02:03", info.BssidState);
    }

    [Theory]
    [InlineData(WifiConnectionStatus.NotConnected, "Not Connected")]
    [InlineData(WifiConnectionStatus.PermissionRequired, "Location permission required")]
    [InlineData(WifiConnectionStatus.Unavailable, "Unavailable")]
    public void Formats_non_connected_states(WifiConnectionStatus status, string expected)
    {
        var info = new WifiConnectionInfo(status);
        Assert.Equal(expected, info.SsidState);
        Assert.Equal(expected, info.BssidState);
        Assert.Equal(expected, info.SecurityState);
        Assert.Equal(expected, info.RandomMacAddressState);
    }

    [Theory]
    [InlineData(1, "Open")]
    [InlineData(2, "Shared Key (WEP)")]
    [InlineData(6, "WPA2-Enterprise")]
    [InlineData(7, "WPA2-Personal")]
    [InlineData(9, "WPA3-Personal")]
    [InlineData(10, "Enhanced Open (OWE)")]
    public void Describes_known_auth_algorithms(int authAlgorithm, string expected) =>
        Assert.Equal(expected, WifiSecurityClassifier.Describe(authAlgorithm));

    [Fact]
    public void Distinguishes_open_system_wep_from_shared_key_wep()
    {
        Assert.Equal("Open System (WEP)", WifiSecurityClassifier.Describe(1, cipherAlgorithm: 1));
        Assert.Equal("Shared Key (WEP)", WifiSecurityClassifier.Describe(2, cipherAlgorithm: 1));
    }

    [Fact]
    public void Reports_unknown_for_an_unrecognised_auth_algorithm() =>
        Assert.Null(WifiSecurityClassifier.Describe(9999));

    [Fact]
    public void Reports_security_type_when_connected()
    {
        var info = new WifiConnectionInfo(WifiConnectionStatus.Connected, "Home Wi-Fi", AuthAlgorithm: 7);
        Assert.Equal("WPA2-Personal", info.SecurityState);
    }

    [Fact]
    public void Reports_unknown_security_when_the_algorithm_is_unrecognised()
    {
        var info = new WifiConnectionInfo(WifiConnectionStatus.Connected, "Home Wi-Fi", AuthAlgorithm: 9999);
        Assert.Equal("Unknown", info.SecurityState);
    }

    [Fact]
    public void Reports_unavailable_security_when_the_algorithm_is_unknown()
    {
        var info = new WifiConnectionInfo(WifiConnectionStatus.Connected, "Home Wi-Fi");
        Assert.Equal("Unavailable", info.SecurityState);
    }

    [Fact]
    public void Reports_the_randomized_address_when_randomization_is_on()
    {
        var info = new WifiConnectionInfo(
            WifiConnectionStatus.Connected,
            "Home Wi-Fi",
            MacRandomizationEnabled: true,
            CurrentMacAddress: "AA:BB:CC:DD:EE:FF");

        Assert.Equal("AA:BB:CC:DD:EE:FF", info.RandomMacAddressState);
    }

    [Fact]
    public void Reports_not_randomized_when_randomization_is_off()
    {
        var info = new WifiConnectionInfo(
            WifiConnectionStatus.Connected, "Home Wi-Fi", MacRandomizationEnabled: false);

        Assert.Equal("Not randomized", info.RandomMacAddressState);
    }

    [Fact]
    public void Reports_unavailable_randomization_state_when_unknown()
    {
        var info = new WifiConnectionInfo(WifiConnectionStatus.Connected, "Home Wi-Fi");
        Assert.Equal("Unavailable", info.RandomMacAddressState);
    }
}
