using WindowsCompanion.Core.Sensors;
using WindowsCompanion_App.Services;

namespace WindowsCompanion.E2E.Tests;

public sealed class WindowsWireGuardStatusProbeTests
{
    [Fact]
    public void Real_windows_probe_returns_a_bounded_state_without_elevation()
    {
        var status = new WindowsWireGuardStatusProbe().Read();

        Assert.True(Enum.IsDefined(status));
    }

    [Fact]
    public void Exact_official_service_and_adapter_metadata_produce_connected()
    {
        var probe = new WindowsWireGuardStatusProbe(
            () =>
            [
                new("WireGuardManager", false),
                new("WireGuardTunnel$Office", true)
            ],
            () =>
            [
                new("office", "WireGuard Tunnel", true)
            ]);

        Assert.Equal(WireGuardStatus.Connected, probe.Read());
    }

    [Fact]
    public void Similar_but_non_official_adapter_is_not_matched()
    {
        var probe = new WindowsWireGuardStatusProbe(
            () => [new("WireGuardTunnel$Office", true)],
            () => [new("Office", "Acme WireGuard Tunnel", true)]);

        Assert.Equal(WireGuardStatus.Disconnected, probe.Read());
    }

    [Fact]
    public void Missing_client_is_unavailable()
    {
        var probe = new WindowsWireGuardStatusProbe(
            () => [],
            () => []);

        Assert.Equal(WireGuardStatus.Unavailable, probe.Read());
    }

    [Fact]
    public void Access_denial_is_unavailable()
    {
        var probe = new WindowsWireGuardStatusProbe(
            () => throw new UnauthorizedAccessException(),
            () => []);

        Assert.Equal(WireGuardStatus.Unavailable, probe.Read());
    }

    [Fact]
    public void Service_page_collection_preserves_resume_handle_and_accumulates_results()
    {
        var requestedHandles = new List<uint>();

        var services = WindowsWireGuardStatusProbe.CollectServicePages(resumeHandle =>
        {
            requestedHandles.Add(resumeHandle);
            return resumeHandle switch
            {
                0 => new(
                    [new WireGuardServiceInfo("WireGuardManager", false)],
                    NextResumeHandle: 42,
                    HasMore: true),
                42 => new(
                    [new WireGuardServiceInfo("WireGuardTunnel$Office", true)],
                    NextResumeHandle: 0,
                    HasMore: false),
                _ => throw new InvalidOperationException("Unexpected resume handle.")
            };
        });

        Assert.Equal([0u, 42u], requestedHandles);
        Assert.Equal(2, services.Count);
        Assert.Contains(services, service => service.Name == "WireGuardTunnel$Office");
    }

    [Fact]
    public void Service_page_collection_rejects_a_non_advancing_resume_handle()
    {
        Assert.Throws<System.ComponentModel.Win32Exception>(() =>
            WindowsWireGuardStatusProbe.CollectServicePages(_ =>
                new([], NextResumeHandle: 0, HasMore: true)));
    }

    [Fact]
    public void Tunnel_names_never_leave_the_status_contract()
    {
        const string privateName = "Private-Office";
        var probe = new WindowsWireGuardStatusProbe(
            () => [new($"WireGuardTunnel${privateName}", true)],
            () => [new(privateName, "WireGuard Tunnel", true)]);

        var status = probe.Read();

        Assert.Equal(WireGuardStatus.Connected, status);
        Assert.DoesNotContain(privateName, WireGuardStatusFormatter.Format(status), StringComparison.Ordinal);
    }
}
