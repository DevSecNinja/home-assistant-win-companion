using WindowsCompanion.Core.App;
using WindowsCompanion.Core.Models;
using Xunit;

namespace WindowsCompanion.Core.Tests;

public class RouteValidatorTests
{
    private const string Internal = "http://homeassistant.local:8123/";
    private const string External = "https://ha.example.com/";
    private const string Instance = "device-registry-id";

    private sealed class FakeProbe : IRouteProbe
    {
        public readonly List<(RouteKind Route, string Url, string? WebhookId)> Calls = new();
        public readonly Dictionary<RouteKind, RouteProbeResult> Results = new();

        public Task<RouteProbeResult> ProbeAsync(
            RouteKind route, string url, string? webhookId, CancellationToken ct = default)
        {
            Calls.Add((route, url, webhookId));
            return Task.FromResult(Results.TryGetValue(route, out var result)
                ? result with { ResolvedUrl = result.ResolvedUrl ?? url }
                : new RouteProbeResult(route, RouteProbeStatus.Ok, url, Instance));
        }

        public FakeProbe With(RouteKind route, RouteProbeStatus status, string? instance = null)
        {
            Results[route] = new RouteProbeResult(route, status, InstanceDeviceId: instance);
            return this;
        }
    }

    private static ServerConfig Current() => new()
    {
        BaseUrl = External,
        WebhookId = "wh-1",
        InstanceDeviceId = Instance
    };

    private static ConnectionSettingsDraft Draft(
        string? internalUrl = Internal,
        string? externalUrl = External,
        ConnectionMode mode = ConnectionMode.Automatic,
        bool acknowledge = false) => new()
    {
        UseSeparateUrls = true,
        InternalUrl = internalUrl,
        ExternalUrl = externalUrl,
        Mode = mode,
        AcknowledgeUnreachable = acknowledge
    };

    [Fact]
    public async Task One_url_is_the_default_configuration()
    {
        var probe = new FakeProbe();
        var draft = new ConnectionSettingsDraft { PrimaryUrl = External };

        var report = await RouteValidator.ValidateAsync(Current(), draft, probe);

        Assert.True(report.CanSave);
        Assert.Single(probe.Calls);
        Assert.Equal(External, probe.Calls[0].Url);
        Assert.Contains("The address reaches", report.Summary);
    }

    [Fact]
    public async Task Applying_one_url_clears_advanced_routing_settings()
    {
        var config = Current();
        config.InternalUrl = Internal;
        config.ExternalUrl = External;
        config.UseSeparateUrls = true;
        config.ConnectionMode = ConnectionMode.PreferInternal;
        config.TrustedNetworks.Ssids.Add("HomeNet");
        var draft = new ConnectionSettingsDraft { PrimaryUrl = External };
        var report = await RouteValidator.ValidateAsync(config, draft, new FakeProbe());

        RouteValidator.Apply(config, draft, report);

        Assert.Equal(External, config.BaseUrl);
        Assert.False(config.UseSeparateUrls);
        Assert.Null(config.InternalUrl);
        Assert.Null(config.ExternalUrl);
        Assert.Empty(config.TrustedNetworks.Ssids);
        Assert.Equal(ConnectionMode.Automatic, config.ConnectionMode);
    }

    [Fact]
    public async Task Both_addresses_on_the_same_instance_can_be_saved()
    {
        var report = await RouteValidator.ValidateAsync(Current(), Draft(), new FakeProbe());

        Assert.True(report.CanSave);
        Assert.Equal(Instance, report.InstanceDeviceId);
        Assert.True(report.For(RouteKind.Internal)!.Validated);
        Assert.True(report.For(RouteKind.External)!.Validated);
    }

    [Fact]
    public async Task An_external_http_address_is_rejected_before_anything_is_probed()
    {
        var probe = new FakeProbe();

        var report = await RouteValidator.ValidateAsync(
            Current(), Draft(externalUrl: "http://ha.example.com"), probe);

        Assert.False(report.CanSave);
        Assert.Empty(probe.Calls);
        Assert.Contains("HTTPS", report.Summary);
    }

    [Fact]
    public async Task An_empty_draft_is_refused()
    {
        var report = await RouteValidator.ValidateAsync(
            Current(), Draft(null, null), new FakeProbe());

        Assert.False(report.CanSave);
        Assert.Contains("at least one", report.Summary);
    }

    [Theory]
    [InlineData(ConnectionMode.InternalOnly, null, External)]
    [InlineData(ConnectionMode.ExternalOnly, Internal, null)]
    public async Task A_mode_that_needs_a_missing_address_is_refused(
        ConnectionMode mode, string? internalUrl, string? externalUrl)
    {
        var probe = new FakeProbe();

        var report = await RouteValidator.ValidateAsync(
            Current(), Draft(internalUrl, externalUrl, mode), probe);

        Assert.False(report.CanSave);
        Assert.Empty(probe.Calls);
    }

    [Fact]
    public async Task Rejected_credentials_ask_for_a_fresh_sign_in_and_change_nothing()
    {
        var probe = new FakeProbe().With(RouteKind.External, RouteProbeStatus.CredentialsRejected);

        var report = await RouteValidator.ValidateAsync(Current(), Draft(), probe);

        Assert.False(report.CanSave);
        Assert.True(report.RequiresSignIn);
    }

    [Fact]
    public async Task A_different_instance_is_refused_so_the_device_and_history_survive()
    {
        var probe = new FakeProbe().With(RouteKind.External, RouteProbeStatus.DifferentInstance);

        var report = await RouteValidator.ValidateAsync(Current(), Draft(), probe);

        Assert.False(report.CanSave);
        Assert.True(report.RequiresSignIn);
        Assert.Contains("different Home Assistant instance", report.Summary);
    }

    [Fact]
    public async Task Two_addresses_reaching_different_instances_are_refused()
    {
        var probe = new FakeProbe()
            .With(RouteKind.Internal, RouteProbeStatus.Ok, "instance-a")
            .With(RouteKind.External, RouteProbeStatus.Ok, "instance-b");

        var report = await RouteValidator.ValidateAsync(Current(), Draft(), probe);

        Assert.False(report.CanSave);
        Assert.True(report.RequiresSignIn);
    }

    [Fact]
    public async Task Addresses_reaching_another_instance_than_this_pc_uses_are_refused()
    {
        var probe = new FakeProbe()
            .With(RouteKind.Internal, RouteProbeStatus.Ok, "somewhere-else")
            .With(RouteKind.External, RouteProbeStatus.Ok, "somewhere-else");

        var report = await RouteValidator.ValidateAsync(Current(), Draft(), probe);

        Assert.False(report.CanSave);
        Assert.True(report.RequiresSignIn);
        Assert.Contains("Remove the server", report.Summary);
    }

    [Fact]
    public async Task An_unreachable_address_needs_an_explicit_confirmation()
    {
        var probe = new FakeProbe().With(RouteKind.Internal, RouteProbeStatus.Unreachable);

        var report = await RouteValidator.ValidateAsync(Current(), Draft(), probe);

        Assert.False(report.CanSave);
        Assert.True(report.RequiresAcknowledgement);
        Assert.False(report.RequiresSignIn);
    }

    [Fact]
    public async Task A_confirmed_unreachable_address_is_saved_alongside_a_working_one()
    {
        var probe = new FakeProbe().With(RouteKind.Internal, RouteProbeStatus.Unreachable);

        var report = await RouteValidator.ValidateAsync(
            Current(), Draft(acknowledge: true), probe);

        Assert.True(report.CanSave);
        Assert.Equal(Instance, report.InstanceDeviceId);
        Assert.Contains("validated when it can be reached", report.Summary);
    }

    [Fact]
    public async Task Confirming_does_not_save_when_neither_address_works()
    {
        var probe = new FakeProbe()
            .With(RouteKind.Internal, RouteProbeStatus.Unreachable)
            .With(RouteKind.External, RouteProbeStatus.Unreachable);

        var report = await RouteValidator.ValidateAsync(
            Current(), Draft(acknowledge: true), probe);

        Assert.False(report.CanSave);
        Assert.Contains("previous configuration was kept", report.Summary);
    }

    [Fact]
    public async Task Something_that_is_not_home_assistant_is_refused()
    {
        var probe = new FakeProbe().With(RouteKind.Internal, RouteProbeStatus.NotHomeAssistant);

        var report = await RouteValidator.ValidateAsync(Current(), Draft(), probe);

        Assert.False(report.CanSave);
        Assert.False(report.RequiresSignIn);
    }

    [Fact]
    public async Task A_single_address_is_a_valid_configuration()
    {
        var report = await RouteValidator.ValidateAsync(
            Current(), Draft(internalUrl: null), new FakeProbe());

        Assert.True(report.CanSave);
        Assert.Null(report.For(RouteKind.Internal)!.Probe);
        Assert.Equal("Not configured.", report.For(RouteKind.Internal)!.Describe());
    }

    [Fact]
    public async Task Validation_uses_the_existing_webhook_and_registers_nothing()
    {
        var probe = new FakeProbe();

        await RouteValidator.ValidateAsync(Current(), Draft(), probe);

        Assert.All(probe.Calls, c => Assert.Equal("wh-1", c.WebhookId));
    }

    [Fact]
    public async Task Apply_writes_the_validated_addresses_and_clears_the_migration_flag()
    {
        var config = Current();
        config.RouteAssignmentPending = true;
        config.InstanceDeviceId = null;
        var draft = Draft(mode: ConnectionMode.PreferInternal) with
        {
            TrustedNetworks = new TrustedNetworkSettings { Ssids = { "HomeNet" } }
        };
        var report = await RouteValidator.ValidateAsync(config, draft, new FakeProbe());

        RouteValidator.Apply(config, draft, report);

        Assert.Equal(Internal, config.InternalUrl);
        Assert.Equal(External, config.ExternalUrl);
        Assert.Equal(ConnectionMode.PreferInternal, config.ConnectionMode);
        Assert.Equal(["HomeNet"], config.TrustedNetworks.Ssids);
        Assert.False(config.RouteAssignmentPending);
        Assert.Equal(Instance, config.InstanceDeviceId);
    }

    [Fact]
    public async Task Apply_refuses_a_report_that_was_not_approved()
    {
        var probe = new FakeProbe().With(RouteKind.External, RouteProbeStatus.DifferentInstance);
        var config = Current();
        var draft = Draft();
        var report = await RouteValidator.ValidateAsync(config, draft, probe);

        Assert.Throws<InvalidOperationException>(() => RouteValidator.Apply(config, draft, report));
        Assert.Null(config.InternalUrl);
        Assert.Null(config.ExternalUrl);
    }

    [Fact]
    public async Task An_accepted_plain_http_internal_address_is_described_as_such()
    {
        var probe = new FakeProbe();
        probe.Results[RouteKind.Internal] =
            new RouteProbeResult(RouteKind.Internal, RouteProbeStatus.Ok, Internal, Instance);

        var report = await RouteValidator.ValidateAsync(Current(), Draft(), probe);

        Assert.Contains("plain HTTP", report.For(RouteKind.Internal)!.Describe());
    }
}
