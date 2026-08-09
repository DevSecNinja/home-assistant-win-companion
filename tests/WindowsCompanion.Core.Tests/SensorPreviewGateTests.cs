using WindowsCompanion.Core.Sensors;

namespace WindowsCompanion.Core.Tests;

public class SensorPreviewGateTests
{
    private static readonly SensorDefinition ConnectionType =
        new("connectivity_connection_type", "Connection Type", "", SensorPrivacy.Benign, false);

    private static readonly SensorDefinition Ipv6 =
        new("ipv6_address", "IPv6 Address", "", SensorPrivacy.Sensitive, false);

    private static readonly SensorDefinition Mac =
        new("mac_address", "MAC Address", "", SensorPrivacy.Sensitive, false);

    private static readonly SensorDefinition[] Definitions = [ConnectionType, Ipv6, Mac];

    private static IReadOnlySet<string> All =>
        Definitions.Select(d => d.UniqueId).ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Withholds_every_sensitive_sensor_until_it_is_enabled()
    {
        var permitted = SensorPreviewGate.Permitted(Definitions, All, new SensorPreferences());

        Assert.Equal(["connectivity_connection_type"], permitted);
    }

    [Fact]
    public void Enabling_one_identifier_does_not_reveal_the_other()
    {
        var preferences = new SensorPreferences();
        preferences.Set("ipv6_address", true);

        var permitted = SensorPreviewGate.Permitted(Definitions, All, preferences);

        Assert.Contains("ipv6_address", permitted);
        Assert.DoesNotContain("mac_address", permitted);
    }

    [Fact]
    public void Never_permits_a_sensor_the_caller_did_not_ask_for()
    {
        var preferences = new SensorPreferences();
        preferences.Set("ipv6_address", true);
        preferences.Set("mac_address", true);

        var permitted = SensorPreviewGate.Permitted(
            Definitions, new HashSet<string> { "mac_address" }, preferences);

        Assert.Equal(["mac_address"], permitted);
    }

    [Fact]
    public void Permits_a_sensitive_sensor_the_user_switched_on()
    {
        var preferences = new SensorPreferences();
        preferences.Set("mac_address", true);
        preferences.Set("ipv6_address", true);

        var permitted = SensorPreviewGate.Permitted(Definitions, All, preferences);

        Assert.Equal(All.OrderBy(id => id), permitted.OrderBy(id => id));
    }
}
