using System.Text.Json;
using HaCompanion.Core.Models;
using Xunit;

namespace HaCompanion.Core.Tests;

public class ServerConfigRouteTests
{
    private const string Internal = "http://homeassistant.local:8123/";
    private const string External = "https://ha.example.com/";

    [Fact]
    public void An_install_from_the_single_url_era_keeps_working_and_asks_to_be_classified()
    {
        var config = new ServerConfig { BaseUrl = External };

        Assert.True(config.MigrateRoutes());

        Assert.True(config.RouteAssignmentPending);
        Assert.Equal(External, config.BaseUrl);
        // Deliberately not guessed: split DNS and reverse proxies make the hostname
        // a poor signal, so nothing is assigned until the user says so.
        Assert.Null(config.InternalUrl);
        Assert.Null(config.ExternalUrl);
    }

    [Fact]
    public void Migration_is_idempotent()
    {
        var config = new ServerConfig { BaseUrl = External };

        Assert.True(config.MigrateRoutes());
        Assert.False(config.MigrateRoutes());
    }

    [Fact]
    public void A_fresh_install_with_no_url_has_nothing_to_migrate()
    {
        var config = new ServerConfig();

        Assert.False(config.MigrateRoutes());
        Assert.False(config.RouteAssignmentPending);
    }

    [Fact]
    public void An_install_that_already_has_routes_is_never_flagged()
    {
        var config = new ServerConfig { BaseUrl = External, ExternalUrl = External };

        Assert.False(config.MigrateRoutes());
        Assert.False(config.RouteAssignmentPending);
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
        Assert.False(config.RouteAssignmentPending);
    }

    [Fact]
    public void Assigning_a_route_resolves_the_migration_prompt()
    {
        var config = new ServerConfig { BaseUrl = External, RouteAssignmentPending = true };

        config.SetRoute(RouteKind.External, External);

        Assert.False(config.RouteAssignmentPending);
        Assert.Equal(External, config.UrlFor(RouteKind.External));
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
            InternalUrl = Internal,
            ExternalUrl = External,
            ConnectionMode = ConnectionMode.PreferInternal,
            LastSuccessfulRoute = RouteKind.Internal,
            InstanceDeviceId = "dev-9",
            TrustedNetworks = new TrustedNetworkSettings { Ssids = { "HomeNet" }, TrustWiredNetworks = true }
        };

        var json = JsonSerializer.Serialize(config);
        var restored = JsonSerializer.Deserialize<ServerConfig>(json)!;

        Assert.Contains("\"PreferInternal\"", json, StringComparison.Ordinal);
        Assert.Equal(ConnectionMode.PreferInternal, restored.ConnectionMode);
        Assert.Equal(RouteKind.Internal, restored.LastSuccessfulRoute);
        Assert.Equal(Internal, restored.InternalUrl);
        Assert.Equal(External, restored.ExternalUrl);
        Assert.Equal("dev-9", restored.InstanceDeviceId);
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
