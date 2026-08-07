using HaCompanion.Core.Sensors;
using Xunit;

namespace HaCompanion.Core.Tests;

public class SystemInfoTests
{
    [Fact]
    public void Loopback_and_tunnel_adapters_do_not_count_as_connected()
    {
        // These are always up; counting them would make an offline machine look wired.
        var adapters = new[] { NetworkAdapterKind.Loopback, NetworkAdapterKind.Tunnel };
        Assert.Equal(NetworkClassifier.NotConnected, NetworkClassifier.Classify(adapters));
    }

    [Fact]
    public void Wireless_wins_over_wired_when_both_are_up()
    {
        var adapters = new[] { NetworkAdapterKind.Wired, NetworkAdapterKind.Wireless };
        Assert.Equal(NetworkClassifier.WiFi, NetworkClassifier.Classify(adapters));
    }

    [Fact]
    public void Wired_only_reports_ethernet()
    {
        var adapters = new[] { NetworkAdapterKind.Loopback, NetworkAdapterKind.Wired };
        Assert.Equal(NetworkClassifier.Ethernet, NetworkClassifier.Classify(adapters));
    }

    [Fact]
    public void No_adapters_reports_not_connected()
    {
        Assert.Equal(NetworkClassifier.NotConnected, NetworkClassifier.Classify([]));
    }

    [Fact]
    public void Windows_11_builds_are_relabelled_from_the_registry_name()
    {
        // Windows 11 still reports "Windows 10 ..." in ProductName.
        var text = OsVersionFormatter.Describe("Windows 10 Pro", "24H2", "26100", "2314", "fallback");
        Assert.Equal("Windows 11 Pro 24H2 26100.2314", text);
    }

    [Fact]
    public void Windows_10_builds_are_left_alone()
    {
        var text = OsVersionFormatter.Describe("Windows 10 Pro", "22H2", "19045", "3803", "fallback");
        Assert.Equal("Windows 10 Pro 22H2 19045.3803", text);
    }

    [Fact]
    public void Missing_parts_are_omitted_rather_than_leaving_gaps()
    {
        var text = OsVersionFormatter.Describe("Windows 11 Pro", null, "26100", null, "fallback");
        Assert.Equal("Windows 11 Pro 26100", text);
    }

    [Fact]
    public void Falls_back_when_the_registry_gives_nothing()
    {
        Assert.Equal("10.0.26100", OsVersionFormatter.Describe(null, null, null, null, "10.0.26100"));
    }

    [Fact]
    public void Boot_time_does_not_jitter_between_reads()
    {
        var calculator = new BootTimeCalculator();
        var first = calculator.Resolve(new DateTimeOffset(2026, 8, 7, 9, 0, 0, 123, TimeSpan.Zero));

        // A few hundred ms of drift per read must not produce a new state, or Home
        // Assistant records a history entry every sync interval.
        var second = calculator.Resolve(new DateTimeOffset(2026, 8, 7, 9, 0, 0, 456, TimeSpan.Zero));
        var third = calculator.Resolve(new DateTimeOffset(2026, 8, 7, 9, 0, 2, 0, TimeSpan.Zero));

        Assert.Equal(first, second);
        Assert.Equal(first, third);
        Assert.Equal(0, first.Millisecond);
    }

    [Fact]
    public void Boot_time_moves_after_a_reboot_or_hibernation()
    {
        var calculator = new BootTimeCalculator();
        var before = calculator.Resolve(new DateTimeOffset(2026, 8, 7, 9, 0, 0, TimeSpan.Zero));
        var after = calculator.Resolve(new DateTimeOffset(2026, 8, 7, 11, 0, 0, TimeSpan.Zero));

        Assert.NotEqual(before, after);
        Assert.Equal(new DateTimeOffset(2026, 8, 7, 11, 0, 0, TimeSpan.Zero), after);
    }
}
