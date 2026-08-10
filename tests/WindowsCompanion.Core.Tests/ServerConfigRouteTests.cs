using System.Text.Json;
using WindowsCompanion.Core.Models;
using Xunit;

namespace WindowsCompanion.Core.Tests;

public class ServerConfigRouteTests
{
    private const string Internal = "http://homeassistant.local:8123/";
    private const string External = "https://ha.example.com/";

    [Fact]
    public void An_install_from_the_single_url_era_stays_in_single_url_mode()
    {
        var config = new ServerConfig { BaseUrl = External };

        Assert.False(config.MigrateRoutes());
        Assert.False(config.UseSeparateUrls);
        Assert.False(config.RouteAssignmentPending);
        Assert.Equal(External, config.BaseUrl);
        Assert.Null(config.InternalUrl);
        Assert.Null(config.ExternalUrl);
    }

    [Fact]
    public void Migration_is_idempotent()
    {
        var config = new ServerConfig { BaseUrl = External };

        Assert.False(config.MigrateRoutes());
        Assert.False(config.MigrateRoutes());
    }

    [Fact]
    public void A_fresh_install_with_no_url_has_nothing_to_migrate()
    {
        var config = new ServerConfig();

        Assert.False(config.MigrateRoutes());
        Assert.False(config.UseSeparateUrls);
        Assert.False(config.RouteAssignmentPending);
    }

    [Fact]
    public void One_route_from_the_first_dual_url_release_becomes_the_single_url()
    {
        var config = new ServerConfig { BaseUrl = External, ExternalUrl = External };

        Assert.True(config.MigrateRoutes());
        Assert.False(config.UseSeparateUrls);
        Assert.Equal(External, config.BaseUrl);
        Assert.Null(config.ExternalUrl);
        Assert.False(config.RouteAssignmentPending);
    }

    [Fact]
    public void Two_existing_routes_preserve_the_opted_in_dual_url_configuration()
    {
        var config = new ServerConfig
        {
            BaseUrl = External,
            InternalUrl = Internal,
            ExternalUrl = External
        };

        Assert.True(config.MigrateRoutes());
        Assert.True(config.UseSeparateUrls);
        Assert.Equal(Internal, config.InternalUrl);
        Assert.Equal(External, config.ExternalUrl);
    }

    [Fact]
    public void An_opted_in_single_route_configuration_stays_opted_in()
    {
        var config = new ServerConfig
        {
            BaseUrl = External,
            ExternalUrl = External,
            UseSeparateUrls = true,
            ConnectionMode = ConnectionMode.ExternalOnly,
            TrustedNetworks = new TrustedNetworkSettings { Ssids = { "HomeNet" } }
        };

        Assert.False(config.MigrateRoutes());
        Assert.True(config.UseSeparateUrls);
        Assert.Equal(ConnectionMode.ExternalOnly, config.ConnectionMode);
        Assert.Equal(["HomeNet"], config.TrustedNetworks.Ssids);
        Assert.Equal(External, config.ExternalUrl);
    }

    [Fact]
    public void A_stale_pending_flag_is_cleared_once_a_route_exists()
    {
        var config = new ServerConfig
        {
            BaseUrl = External,
            ExternalUrl = External,
            RouteAssignmentPending = true
        };

        Assert.True(config.MigrateRoutes());
        Assert.False(config.UseSeparateUrls);
        Assert.False(config.RouteAssignmentPending);
        Assert.Equal(External, config.BaseUrl);
        Assert.Null(config.ExternalUrl);
    }

    [Fact]
    public void Setting_a_single_url_clears_route_specific_settings()
    {
        var config = new ServerConfig
        {
            BaseUrl = External,
            InternalUrl = Internal,
            ExternalUrl = External,
            UseSeparateUrls = true
        };

        config.SetSingleUrl(External);

        Assert.False(config.UseSeparateUrls);
        Assert.False(config.RouteAssignmentPending);
        Assert.Null(config.InternalUrl);
        Assert.Null(config.ExternalUrl);
    }

    [Fact]
    public void Blank_addresses_are_stored_as_unset()
    {
        var config = new ServerConfig();

        config.SetRoute(RouteKind.Internal, "   ");

        Assert.Null(config.InternalUrl);
        Assert.False(config.HasRoute(RouteKind.Internal));
        Assert.Empty(config.ConfiguredRoutes());
    }

    [Fact]
    public void Configured_routes_are_reported_in_a_stable_order()
    {
        var config = new ServerConfig { InternalUrl = Internal, ExternalUrl = External };

        Assert.Equal([RouteKind.Internal, RouteKind.External], config.ConfiguredRoutes());
    }

    [Fact]
    public void Activating_a_route_keeps_the_legacy_base_url_in_step()
    {
        var config = new ServerConfig { BaseUrl = External, InternalUrl = Internal, ExternalUrl = External };
        var at = DateTimeOffset.UnixEpoch;

        config.SetActiveRoute(RouteKind.Internal, at);

        Assert.Equal(Internal, config.BaseUrl);
        Assert.Equal(RouteKind.Internal, config.LastSuccessfulRoute);
        Assert.Equal(at, config.LastSuccessfulRouteAt);
        Assert.True(config.IsValid());
    }

    [Fact]
    public void Activating_an_unconfigured_route_is_refused()
    {
        var config = new ServerConfig { BaseUrl = External, ExternalUrl = External };

        Assert.Throws<InvalidOperationException>(
            () => config.SetActiveRoute(RouteKind.Internal, DateTimeOffset.UnixEpoch));
        Assert.Equal(External, config.BaseUrl);
    }

    [Fact]
    public void Routing_settings_round_trip_through_settings_json_readably()
    {
        var config = new ServerConfig
        {
            BaseUrl = Internal,
            UseSeparateUrls = true,
            InternalUrl = Internal,
            ExternalUrl = External,
            ConnectionMode = ConnectionMode.PreferInternal,
            LastSuccessfulRoute = RouteKind.Internal,
            InstanceDeviceId = "dev-9",
            TrustedNetworks = new TrustedNetworkSettings
            {
                Cidrs = { "192.168.50.0/24", "fd12:3456::/48" },
                Ssids = { "HomeNet" },
                TrustWiredNetworks = true
            }
        };

        var json = JsonSerializer.Serialize(config);
        var restored = JsonSerializer.Deserialize<ServerConfig>(json)!;

        Assert.Contains("\"PreferInternal\"", json, StringComparison.Ordinal);
        Assert.True(restored.UseSeparateUrls);
        Assert.Equal(ConnectionMode.PreferInternal, restored.ConnectionMode);
        Assert.Equal(RouteKind.Internal, restored.LastSuccessfulRoute);
        Assert.Equal(Internal, restored.InternalUrl);
        Assert.Equal(External, restored.ExternalUrl);
        Assert.Equal("dev-9", restored.InstanceDeviceId);
        Assert.Equal(["192.168.50.0/24", "fd12:3456::/48"], restored.TrustedNetworks.Cidrs);
        Assert.Equal(["HomeNet"], restored.TrustedNetworks.Ssids);
        Assert.True(restored.TrustedNetworks.TrustWiredNetworks);
    }

    [Fact]
    public void An_unknown_connection_mode_in_settings_json_does_not_break_startup()
    {
        var json = """{"BaseUrl":"https://ha.example.com/","ConnectionMode":"Automatic"}""";

        var restored = JsonSerializer.Deserialize<ServerConfig>(json)!;

        Assert.Equal(ConnectionMode.Automatic, restored.ConnectionMode);
        Assert.False(restored.RouteAssignmentPending);
    }
}
