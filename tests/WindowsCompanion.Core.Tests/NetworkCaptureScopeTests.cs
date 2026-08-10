using WindowsCompanion.Core.Sensors;

namespace WindowsCompanion.Core.Tests;

/// <summary>
/// The collection budget: what the companion is allowed to look at for a given set
/// of enabled sensors. Nothing enabled must mean nothing enumerated and no route
/// probe, and connection type on its own must never reach an identifier.
/// </summary>
public class NetworkCaptureScopeTests
{
    [Fact]
    public void Nothing_enabled_permits_no_enumeration()
    {
        Assert.Equal(NetworkCaptureScope.None, NetworkSensors.ScopeFor(Set()));
        Assert.Equal(NetworkCaptureScope.None, NetworkSensors.ScopeFor(null));
    }

    [Fact]
    public void An_unrelated_sensor_permits_no_enumeration()
    {
        Assert.Equal(NetworkCaptureScope.None, NetworkSensors.ScopeFor(Set("battery_state", "active")));
    }

    [Fact]
    public void Connection_type_alone_permits_adapter_kinds_only()
    {
        Assert.Equal(
            NetworkCaptureScope.ConnectionTypeOnly,
            NetworkSensors.ScopeFor(Set(NetworkSensors.ConnectionTypeId)));
    }

    [Theory]
    [InlineData(NetworkSensors.IpAddressId)]
    [InlineData(NetworkSensors.Ipv6AddressId)]
    [InlineData(NetworkSensors.MacAddressId)]
    [InlineData(NetworkSensors.LanMacAddressId)]
    [InlineData(NetworkSensors.WlanMacAddressId)]
    [InlineData(NetworkSensors.GatewayAddressId)]
    [InlineData(NetworkSensors.DnsServersId)]
    public void Any_enabled_identifier_permits_a_full_capture(string identifier)
    {
        Assert.Equal(NetworkCaptureScope.Full, NetworkSensors.ScopeFor(Set(identifier)));
    }

    [Fact]
    public void Disabling_the_identifiers_drops_back_to_adapter_kinds_only()
    {
        var enabled = new HashSet<string>(StringComparer.Ordinal)
        {
            NetworkSensors.ConnectionTypeId,
            NetworkSensors.Ipv6AddressId,
            NetworkSensors.MacAddressId
        };

        Assert.Equal(NetworkCaptureScope.Full, NetworkSensors.ScopeFor(enabled));

        enabled.Remove(NetworkSensors.Ipv6AddressId);
        enabled.Remove(NetworkSensors.MacAddressId);

        Assert.Equal(NetworkCaptureScope.ConnectionTypeOnly, NetworkSensors.ScopeFor(enabled));

        enabled.Remove(NetworkSensors.ConnectionTypeId);

        Assert.Equal(NetworkCaptureScope.None, NetworkSensors.ScopeFor(enabled));
    }

    private static IReadOnlySet<string> Set(params string[] ids) =>
        ids.ToHashSet(StringComparer.Ordinal);
}
