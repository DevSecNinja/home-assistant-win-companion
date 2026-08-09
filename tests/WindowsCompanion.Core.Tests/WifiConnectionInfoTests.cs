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
    }
}
