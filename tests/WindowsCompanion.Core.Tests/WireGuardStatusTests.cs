using WindowsCompanion.Core.Sensors;

namespace WindowsCompanion.Core.Tests;

public sealed class WireGuardStatusTests
{
    [Theory]
    [InlineData(WireGuardStatus.Connected, "connected")]
    [InlineData(WireGuardStatus.Disconnected, "disconnected")]
    [InlineData(WireGuardStatus.Unavailable, "unavailable")]
    public void Formatter_returns_stable_lowercase_states(WireGuardStatus status, string expected) =>
        Assert.Equal(expected, WireGuardStatusFormatter.Format(status));

    [Fact]
    public void Matching_running_service_and_operational_adapter_is_connected()
    {
        var status = WireGuardStatusClassifier.Classify(
            inspectionSucceeded: true,
            clientDetected: true,
            runningTunnels: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Office" },
            operationalAdapters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "office" });

        Assert.Equal(WireGuardStatus.Connected, status);
    }

    [Fact]
    public void Detectable_client_without_a_matching_pair_is_disconnected()
    {
        var status = WireGuardStatusClassifier.Classify(
            inspectionSucceeded: true,
            clientDetected: true,
            runningTunnels: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Office" },
            operationalAdapters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Travel" });

        Assert.Equal(WireGuardStatus.Disconnected, status);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Failed_inspection_or_missing_client_is_unavailable(
        bool inspectionSucceeded,
        bool clientDetected)
    {
        var status = WireGuardStatusClassifier.Classify(
            inspectionSucceeded,
            clientDetected,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal(WireGuardStatus.Unavailable, status);
    }
}
