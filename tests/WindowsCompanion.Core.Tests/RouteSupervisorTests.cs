using WindowsCompanion.Core.Abstractions;
using WindowsCompanion.Core.App;
using WindowsCompanion.Core.Models;
using Xunit;

namespace WindowsCompanion.Core.Tests;

public class RouteSupervisorTests
{
    private const string Internal = "http://homeassistant.local:8123/";
    private const string External = "https://ha.example.com/";
    private const string Instance = "device-registry-id";

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;
    }

    private sealed class FakeProbe : IRouteProbe
    {
        public readonly List<RouteKind> Probed = new();
        public readonly Dictionary<RouteKind, RouteProbeResult> Results = new();

        public Task<RouteProbeResult> ProbeAsync(
            RouteKind route, string url, string? webhookId, CancellationToken ct = default)
        {
            Probed.Add(route);
            return Task.FromResult(Results.TryGetValue(route, out var result)
                ? result with { ResolvedUrl = result.ResolvedUrl ?? url }
                : new RouteProbeResult(route, RouteProbeStatus.Unreachable));
        }

        public FakeProbe Ok(RouteKind route, string? instance = Instance)
        {
            Results[route] = new RouteProbeResult(route, RouteProbeStatus.Ok, InstanceDeviceId: instance);
            return this;
        }

        public FakeProbe Fails(RouteKind route, RouteProbeStatus status = RouteProbeStatus.Unreachable)
        {
            Results[route] = new RouteProbeResult(route, status);
            return this;
        }
    }

    private static ServerConfig Config(ConnectionMode mode = ConnectionMode.Automatic)
    {
        var config = new ServerConfig
        {
            BaseUrl = External,
            InternalUrl = Internal,
            ExternalUrl = External,
            ConnectionMode = mode,
            WebhookId = "wh-1",
            InstanceDeviceId = Instance
        };
        config.TrustedNetworks.Ssids.Add("HomeNet");
        return config;
    }

    private static NetworkContext Home => new(NetworkKind.Wireless, "HomeNet");
    private static NetworkContext Cafe => new(NetworkKind.Wireless, "CafeGuest");

    [Fact]
    public void Duplicate_adapter_snapshots_are_the_same_routing_profile()
    {
        var first = new NetworkContext(
            NetworkKind.Wireless,
            "HomeNet",
            "AA:BB:CC:DD:EE:FF",
            VpnActive: true,
            LocalAddresses: ["192.0.2.20", "2001:db8::20"]);
        var duplicate = first with
        {
            Bssid = "aa:bb:cc:dd:ee:ff",
            LocalAddresses = ["2001:db8::20", "192.0.2.20"]
        };

        Assert.True(first.HasSameRoutingProfile(duplicate));
        Assert.False(first.HasSameRoutingProfile(duplicate with { Ssid = "OtherNet" }));
        Assert.False(first.HasSameRoutingProfile(duplicate with { Kind = NetworkKind.Offline }));
    }

    [Fact]
    public async Task Startup_on_a_trusted_network_activates_the_internal_address()
    {
        var config = Config();
        var probe = new FakeProbe().Ok(RouteKind.Internal).Ok(RouteKind.External);
        var supervisor = new RouteSupervisor(config, probe, new MutableClock());

        var decision = await supervisor.EvaluateAsync(Home, RouteTrigger.Startup);

        Assert.Equal(RouteDecisionKind.Activated, decision.Kind);
        Assert.Equal(RouteKind.Internal, decision.Route);
        Assert.Equal(Internal, supervisor.ActiveUrl);
        Assert.Equal(RouteStatus.Internal, supervisor.Status);
        Assert.Equal(Internal, config.BaseUrl);
        Assert.Equal(RouteKind.Internal, config.LastSuccessfulRoute);
        // The external address is never touched once the preferred one works.
        Assert.Equal([RouteKind.Internal], probe.Probed);
    }

    [Fact]
    public async Task Away_from_home_the_internal_address_is_never_probed()
    {
        var probe = new FakeProbe().Ok(RouteKind.Internal).Ok(RouteKind.External);
        var supervisor = new RouteSupervisor(Config(), probe, new MutableClock());

        var decision = await supervisor.EvaluateAsync(Cafe, RouteTrigger.Startup);

        Assert.Equal(RouteKind.External, decision.Route);
        Assert.DoesNotContain(RouteKind.Internal, probe.Probed);
    }

    [Fact]
    public async Task An_unreachable_preferred_address_falls_through_to_the_other_one()
    {
        var probe = new FakeProbe().Fails(RouteKind.Internal).Ok(RouteKind.External);
        var supervisor = new RouteSupervisor(Config(), probe, new MutableClock());

        var decision = await supervisor.EvaluateAsync(Home, RouteTrigger.Startup);

        Assert.Equal(RouteDecisionKind.Activated, decision.Kind);
        Assert.Equal(RouteKind.External, decision.Route);
        Assert.Equal([RouteKind.Internal, RouteKind.External], probe.Probed);
    }

    [Fact]
    public async Task A_route_answering_as_a_different_instance_is_refused()
    {
        var probe = new FakeProbe().Ok(RouteKind.Internal, "someone-elses-instance").Ok(RouteKind.External);
        var config = Config();
        var supervisor = new RouteSupervisor(config, probe, new MutableClock());

        var decision = await supervisor.EvaluateAsync(Home, RouteTrigger.Startup);

        Assert.Equal(RouteKind.External, decision.Route);
        Assert.Equal(Instance, config.InstanceDeviceId);
        var refused = Assert.Single(decision.Probes!, p => p.Route == RouteKind.Internal);
        Assert.Equal(RouteProbeStatus.DifferentInstance, refused.Status);
    }

    [Fact]
    public async Task Nothing_usable_leaves_the_previous_configuration_untouched()
    {
        var config = Config();
        var probe = new FakeProbe().Fails(RouteKind.Internal).Fails(RouteKind.External);
        var supervisor = new RouteSupervisor(config, probe, new MutableClock());

        var decision = await supervisor.EvaluateAsync(Home, RouteTrigger.Startup);

        Assert.Equal(RouteDecisionKind.NoRouteAvailable, decision.Kind);
        Assert.Equal(RouteStatus.Offline, supervisor.Status);
        Assert.Equal(External, config.BaseUrl);
        Assert.Equal(Internal, config.InternalUrl);
        Assert.Equal(External, config.ExternalUrl);
    }

    [Fact]
    public async Task Offline_never_probes_anything()
    {
        var probe = new FakeProbe().Ok(RouteKind.Internal).Ok(RouteKind.External);
        var supervisor = new RouteSupervisor(Config(), probe, new MutableClock());

        var decision = await supervisor.EvaluateAsync(NetworkContext.Offline, RouteTrigger.NetworkChanged);

        Assert.Equal(RouteDecisionKind.NoRouteAvailable, decision.Kind);
        Assert.Empty(probe.Probed);
    }

    [Fact]
    public async Task A_network_change_within_the_cooldown_does_not_move_a_working_route()
    {
        var clock = new MutableClock();
        var probe = new FakeProbe().Ok(RouteKind.Internal).Ok(RouteKind.External);
        var supervisor = new RouteSupervisor(Config(), probe, clock);
        await supervisor.EvaluateAsync(Cafe, RouteTrigger.Startup);
        probe.Probed.Clear();

        // Coming home makes the internal address preferable, but a route proven
        // 30 seconds ago is not abandoned because the adapter blinked.
        clock.UtcNow = clock.UtcNow.AddSeconds(30);
        var decision = await supervisor.EvaluateAsync(Home, RouteTrigger.NetworkChanged);

        Assert.Equal(RouteDecisionKind.Deferred, decision.Kind);
        Assert.Equal(RouteKind.External, supervisor.ActiveRoute);
        Assert.Empty(probe.Probed);
    }

    [Fact]
    public async Task Once_the_cooldown_expires_a_network_change_can_switch_the_route()
    {
        var clock = new MutableClock();
        var probe = new FakeProbe().Ok(RouteKind.Internal).Ok(RouteKind.External);
        var supervisor = new RouteSupervisor(Config(), probe, clock);
        await supervisor.EvaluateAsync(Cafe, RouteTrigger.Startup);

        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        var decision = await supervisor.EvaluateAsync(Home, RouteTrigger.NetworkChanged);

        Assert.Equal(RouteDecisionKind.Activated, decision.Kind);
        Assert.Equal(RouteKind.Internal, supervisor.ActiveRoute);
    }

    [Fact]
    public async Task A_real_connection_failure_bypasses_the_cooldown()
    {
        var clock = new MutableClock();
        var probe = new FakeProbe().Ok(RouteKind.Internal).Ok(RouteKind.External);
        var supervisor = new RouteSupervisor(Config(), probe, clock);
        await supervisor.EvaluateAsync(Home, RouteTrigger.Startup);

        probe.Fails(RouteKind.Internal);
        clock.UtcNow = clock.UtcNow.AddSeconds(5);
        var decision = await supervisor.EvaluateAsync(Home, RouteTrigger.ConnectionFailed);

        Assert.Equal(RouteDecisionKind.Activated, decision.Kind);
        Assert.Equal(RouteKind.External, supervisor.ActiveRoute);
    }

    [Fact]
    public async Task Periodic_checks_are_rate_limited_so_neither_server_is_polled()
    {
        var clock = new MutableClock();
        var probe = new FakeProbe().Ok(RouteKind.Internal).Ok(RouteKind.External);
        var supervisor = new RouteSupervisor(Config(), probe, clock);
        await supervisor.EvaluateAsync(Home, RouteTrigger.Startup);
        probe.Probed.Clear();

        clock.UtcNow = clock.UtcNow.AddSeconds(5);
        var decision = await supervisor.EvaluateAsync(Cafe, RouteTrigger.Periodic);

        Assert.Equal(RouteDecisionKind.Deferred, decision.Kind);
        Assert.Empty(probe.Probed);
    }

    [Fact]
    public async Task Staying_on_the_preferred_route_reports_unchanged_without_probing()
    {
        var clock = new MutableClock();
        var probe = new FakeProbe().Ok(RouteKind.Internal).Ok(RouteKind.External);
        var supervisor = new RouteSupervisor(Config(), probe, clock);
        await supervisor.EvaluateAsync(Home, RouteTrigger.Startup);
        probe.Probed.Clear();

        clock.UtcNow = clock.UtcNow.AddMinutes(10);
        var decision = await supervisor.EvaluateAsync(Home, RouteTrigger.NetworkChanged);

        Assert.Equal(RouteDecisionKind.Unchanged, decision.Kind);
        Assert.Empty(probe.Probed);
    }

    [Fact]
    public async Task Switching_route_never_rewrites_the_registration_details()
    {
        var config = Config();
        var probe = new FakeProbe().Ok(RouteKind.Internal).Ok(RouteKind.External);
        var supervisor = new RouteSupervisor(config, probe, new MutableClock());

        await supervisor.EvaluateAsync(Home, RouteTrigger.Startup);
        await supervisor.EvaluateAsync(Cafe, RouteTrigger.ConnectionFailed);

        Assert.Equal(RouteKind.External, supervisor.ActiveRoute);
        Assert.Equal("wh-1", config.WebhookId);
        Assert.Equal(Instance, config.InstanceDeviceId);
    }

    [Fact]
    public async Task The_instance_id_is_learned_when_it_was_not_known_yet()
    {
        var config = Config();
        config.InstanceDeviceId = null;
        var probe = new FakeProbe().Ok(RouteKind.Internal);
        var supervisor = new RouteSupervisor(config, probe, new MutableClock());

        await supervisor.EvaluateAsync(Home, RouteTrigger.Startup);

        Assert.Equal(Instance, config.InstanceDeviceId);
    }

    [Fact]
    public async Task Adopting_a_resumed_route_reports_it_without_probing()
    {
        var probe = new FakeProbe();
        var supervisor = new RouteSupervisor(Config(), probe, new MutableClock());

        supervisor.Adopt(RouteKind.External, External);

        Assert.Equal(RouteStatus.External, supervisor.Status);
        Assert.Equal(External, supervisor.ActiveUrl);
        Assert.Empty(probe.Probed);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Activation_raises_the_event_once_per_actual_change()
    {
        var clock = new MutableClock();
        var probe = new FakeProbe().Ok(RouteKind.Internal).Ok(RouteKind.External);
        var supervisor = new RouteSupervisor(Config(), probe, clock);
        var activations = new List<RouteKind?>();
        supervisor.RouteActivated += d => activations.Add(d.Route);

        await supervisor.EvaluateAsync(Home, RouteTrigger.Startup);
        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        await supervisor.EvaluateAsync(Home, RouteTrigger.UserRequested);

        Assert.Equal([RouteKind.Internal], activations);
    }
}
