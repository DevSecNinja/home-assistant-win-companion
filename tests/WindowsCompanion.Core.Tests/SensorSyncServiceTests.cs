using WindowsCompanion.Core.Abstractions;
using WindowsCompanion.Core.HomeAssistant;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;
using Xunit;

namespace WindowsCompanion.Core.Tests;

public class SensorSyncServiceTests
{
    private sealed class FakeStatus : ISystemStatusProvider
    {
        public SystemStatus Status = new(true, 50, PowerState.Discharging);
        public SystemStatus GetStatus() => Status;
    }

    private sealed class FakeClient : IHomeAssistantClient
    {
        public readonly List<string> Registered = new();
        public readonly List<(string Id, bool? Disabled)> RegisterCalls = new();
        public readonly List<IReadOnlyList<Sensor>> Batches = new();
        public readonly List<LocationUpdate> LocationUpdates = new();
        public int Updates;
        public bool FailNextUpdate;
        public HomeAssistantRejectedException? RejectNextUpdate;

        public Task<bool> ValidateAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<DeviceRegistrationResponse> RegisterDeviceAsync(DeviceRegistrationRequest request, CancellationToken ct = default)
            => Task.FromResult(new DeviceRegistrationResponse { WebhookId = "wh" });

        public Task UpdateRegistrationAsync(string webhookId, DeviceRegistrationRequest request, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RegisterSensorAsync(string webhookId, Sensor sensor, CancellationToken ct = default)
        {
            Registered.Add(sensor.UniqueId);
            RegisterCalls.Add((sensor.UniqueId, sensor.Disabled));
            return Task.CompletedTask;
        }

        public Task UpdateSensorsAsync(string webhookId, IReadOnlyList<Sensor> sensors, CancellationToken ct = default)
        {
            Updates++;
            Batches.Add(sensors);
            if (FailNextUpdate)
            {
                FailNextUpdate = false;
                throw new HttpRequestException("boom");
            }
            if (RejectNextUpdate is { } rejection)
            {
                RejectNextUpdate = null;
                throw rejection;
            }
            return Task.CompletedTask;
        }

        public Task<HaInstanceInfo?> GetInstanceInfoAsync(string webhookId, CancellationToken ct = default)
            => Task.FromResult<HaInstanceInfo?>(new HaInstanceInfo { DeviceId = "device" });

        public Task UpdateLocationAsync(string webhookId, LocationUpdate location, CancellationToken ct = default)
        {
            LocationUpdates.Add(location);
            return Task.CompletedTask;
        }

        public Task<HaConfigInfo?> GetConfigAsync(CancellationToken ct = default)
            => Task.FromResult<HaConfigInfo?>(null);
    }

    private static SensorCatalog BatteryCatalog(SensorPreferences? prefs = null) =>
        new(new ISensorSource[] { new BatterySensorSource(new FakeStatus()) }, prefs ?? new SensorPreferences());

    [Fact]
    public async Task Sync_registers_each_sensor_once_then_updates()
    {
        var client = new FakeClient();
        var svc = new SensorSyncService(client, BatteryCatalog());

        await svc.SyncAsync("wh", SensorReadContext.Periodic);
        await svc.SyncAsync("wh", SensorReadContext.Periodic);

        // Three unique sensors registered exactly once each across two syncs.
        Assert.Equal(3, client.Registered.Count);
        Assert.Equal(2, client.Updates);
    }

    [Fact]
    public async Task Failed_update_forces_reregistration_on_next_sync()
    {
        var client = new FakeClient();
        var svc = new SensorSyncService(client, BatteryCatalog());

        await svc.SyncAsync("wh", SensorReadContext.Periodic);                 // registers 3, update ok
        client.FailNextUpdate = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => svc.SyncAsync("wh", SensorReadContext.Periodic));
        await svc.SyncAsync("wh", SensorReadContext.Periodic);                 // must re-register the 3 sensors

        Assert.Equal(6, client.Registered.Count);  // 3 + 3 after the forced reset
    }

    [Fact]
    public async Task Not_registered_rejection_forces_reregistration_on_next_sync()
    {
        var client = new FakeClient();
        var svc = new SensorSyncService(client, BatteryCatalog());

        await svc.SyncAsync("wh", SensorReadContext.Periodic);
        client.RejectNextUpdate = new HomeAssistantRejectedException(
            "not registered", sensorsUnregistered: true);

        await Assert.ThrowsAsync<HomeAssistantRejectedException>(
            () => svc.SyncAsync("wh", SensorReadContext.Periodic));
        await svc.SyncAsync("wh", SensorReadContext.Periodic);

        Assert.Equal(6, client.Registered.Count);
    }

    [Fact]
    public async Task Invalid_format_rejection_does_not_reregister_known_sensors()
    {
        var client = new FakeClient();
        var svc = new SensorSyncService(client, BatteryCatalog());

        await svc.SyncAsync("wh", SensorReadContext.Periodic);
        client.RejectNextUpdate = new HomeAssistantRejectedException(
            "invalid format", sensorsUnregistered: false);

        await Assert.ThrowsAsync<HomeAssistantRejectedException>(
            () => svc.SyncAsync("wh", SensorReadContext.Periodic));
        await svc.SyncAsync("wh", SensorReadContext.Periodic);

        Assert.Equal(3, client.Registered.Count);
    }

    [Fact]
    public async Task Disabled_sensors_are_not_registered_or_sent()
    {
        var prefs = new SensorPreferences();
        prefs.Set(BatterySensorProvider.BatteryStateId, false);

        var client = new FakeClient();
        var svc = new SensorSyncService(client, BatteryCatalog(prefs));

        await svc.SyncAsync("wh", SensorReadContext.Periodic);

        Assert.Equal(
            new[] { BatterySensorProvider.BatteryLevelId, BatterySensorProvider.AcPowerId },
            client.Registered);
        var batch = Assert.Single(client.Batches);
        Assert.Equal(
            new[] { BatterySensorProvider.BatteryLevelId, BatterySensorProvider.AcPowerId },
            batch.Select(s => s.UniqueId));
    }

    [Fact]
    public async Task A_sensor_removed_from_the_app_is_retired_on_the_next_start()
    {
        // Simulates upgrading to a version that no longer has this sensor: Home
        // Assistant knows it (persisted registry), but the catalog no longer
        // produces it. Without retirement the entity would sit in Home Assistant
        // showing its last value forever.
        var registered = new Dictionary<string, RegisteredSensor>(StringComparer.Ordinal)
        {
            ["connectivity_ssid"] = new() { Type = "sensor", Name = "SSID" }
        };

        var persisted = 0;
        var client = new FakeClient();
        var svc = new SensorSyncService(client, BatteryCatalog(), registered, () => persisted++);

        await svc.SyncAsync("wh", SensorReadContext.Periodic);

        var retire = Assert.Single(client.RegisterCalls, c => c.Id == "connectivity_ssid");
        Assert.True(retire.Disabled);

        // Forgotten locally too, so it is not retired again on every sync.
        Assert.False(registered.ContainsKey("connectivity_ssid"));
        Assert.True(persisted > 0);

        await svc.SyncAsync("wh", SensorReadContext.Periodic);
        Assert.Single(client.RegisterCalls, c => c.Id == "connectivity_ssid");
    }

    [Fact]
    public async Task Registered_sensors_are_persisted_so_they_survive_a_restart()
    {
        var registered = new Dictionary<string, RegisteredSensor>(StringComparer.Ordinal);
        var client = new FakeClient();
        var svc = new SensorSyncService(client, BatteryCatalog(), registered, persist: null);

        await svc.SyncAsync("wh", SensorReadContext.Periodic);

        Assert.Equal(
            new[] { BatterySensorProvider.AcPowerId, BatterySensorProvider.BatteryLevelId, BatterySensorProvider.BatteryStateId },
            registered.Keys.OrderBy(k => k).ToArray());
        Assert.Equal("Battery Level", registered[BatterySensorProvider.BatteryLevelId].Name);
        Assert.Equal("sensor", registered[BatterySensorProvider.BatteryLevelId].Type);
    }

    [Fact]
    public async Task Switching_a_sensor_off_disables_its_entity_via_register_sensor()
    {
        var prefs = new SensorPreferences();
        var catalog = BatteryCatalog(prefs);
        var client = new FakeClient();
        var svc = new SensorSyncService(client, catalog);

        await svc.SyncAsync("wh", SensorReadContext.Periodic);      // both reported
        catalog.SetEnabled(BatterySensorProvider.BatteryStateId, false);
        await svc.SyncAsync("wh", SensorReadContext.Periodic);      // battery_state disabled
        await svc.SyncAsync("wh", SensorReadContext.Periodic);      // and not repeated

        // Home Assistant only honours "disabled" on register_sensor, never on
        // update_sensor_states, so the disable must go out as a re-registration.
        var disables = client.RegisterCalls.Where(c => c.Disabled == true).Select(c => c.Id).ToList();
        Assert.Equal(new[] { BatterySensorProvider.BatteryStateId }, disables);

        // Enabling must be explicit too, or a previously disabled entity stays off.
        Assert.All(
            client.RegisterCalls.Where(c => c.Disabled != true),
            c => Assert.False(c.Disabled ?? true));

        // The disable is sent once, not on every subsequent sync.
        Assert.Single(client.RegisterCalls, c => c.Disabled == true);

        // Disabled sensors are never part of the state batch.
        Assert.DoesNotContain(client.Batches.SelectMany(b => b),
            s => s.UniqueId == BatterySensorProvider.BatteryStateId && s.Disabled == true);
    }

    [Fact]
    public async Task Long_string_states_are_truncated_to_the_home_assistant_limit()
    {
        var source = new StubSource("long_text", new string('x', 400));
        var catalog = new SensorCatalog(new ISensorSource[] { source }, new SensorPreferences());
        var client = new FakeClient();
        var svc = new SensorSyncService(client, catalog);

        await svc.SyncAsync("wh", SensorReadContext.Periodic);

        var sensor = Assert.Single(Assert.Single(client.Batches));
        Assert.Equal(255, ((string)sensor.State!).Length);
    }

    private sealed class StubSource : ISensorSource
    {
        private readonly string _id;
        private readonly object _state;

        public StubSource(string id, object state)
        {
            _id = id;
            _state = state;
            Definitions = new[]
            {
                new SensorDefinition(id, id, "stub", SensorPrivacy.Benign, EnabledByDefault: true)
            };
        }

        public IReadOnlyList<SensorDefinition> Definitions { get; }

        public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context) =>
            enabled.Contains(_id)
                ? new[] { new Sensor { UniqueId = _id, Type = "sensor", Name = _id, State = _state } }
                : Array.Empty<Sensor>();

        public void Start(Action onChanged) { }
        public void Stop() { }
    }

    private sealed class FakeLocationProvider : ILocationProvider
    {
        public LocationResult Result = LocationResult.Ready(47.398, 8.5451, 12.0);
        public Task<LocationResult> GetLocationAsync(CancellationToken ct = default) =>
            Task.FromResult(Result);
    }

    private static SensorCatalog LocationPlusBatteryCatalog(
        FakeLocationProvider locationProvider, SensorPreferences? prefs = null)
    {
        var p = prefs ?? new SensorPreferences();
        // Location is sensitive+opt-in, so enable it explicitly for tests.
        p.Set(LocationSensorSource.LocationId, true);
        return new SensorCatalog(
            new ISensorSource[]
            {
                new BatterySensorSource(new FakeStatus()),
                new LocationSensorSource(locationProvider, p)
            }, p);
    }

    [Fact]
    public async Task Location_is_excluded_from_sensor_batch_and_sent_via_update_location()
    {
        var locProvider = new FakeLocationProvider();
        var client = new FakeClient();
        var catalog = LocationPlusBatteryCatalog(locProvider);
        catalog.Start(() => { });
        // Give the poll loop time to fetch the first reading.
        await catalog.RefreshAsync();
        var svc = new SensorSyncService(client, catalog);

        await svc.SyncAsync("wh", SensorReadContext.Periodic);

        // Location must not appear in the sensor registration or update batch.
        Assert.DoesNotContain(client.Registered, id => id == LocationSensorSource.LocationId);
        Assert.All(client.Batches, batch =>
            Assert.DoesNotContain(batch, s => s.UniqueId == LocationSensorSource.LocationId));

        // Location should be sent via update_location.
        var loc = Assert.Single(client.LocationUpdates);
        Assert.True(loc.HasFix);
        Assert.Equal(47.398, loc.Latitude);
        Assert.Equal(8.5451, loc.Longitude);
        Assert.Equal(12, loc.GpsAccuracy);

        catalog.Stop();
    }

    [Fact]
    public async Task Location_sends_location_name_when_no_fix_available()
    {
        var locProvider = new FakeLocationProvider
        {
            Result = LocationResult.Unavailable(LocationStatus.PermissionDenied)
        };
        var client = new FakeClient();
        var catalog = LocationPlusBatteryCatalog(locProvider);
        catalog.Start(() => { });
        await catalog.RefreshAsync();
        var svc = new SensorSyncService(client, catalog);

        await svc.SyncAsync("wh", SensorReadContext.Periodic);

        var loc = Assert.Single(client.LocationUpdates);
        Assert.False(loc.HasFix);
        Assert.Equal("not_home", loc.LocationName);

        catalog.Stop();
    }

    [Fact]
    public async Task Legacy_location_sensor_in_registry_is_retired()
    {
        var locProvider = new FakeLocationProvider();
        var registered = new Dictionary<string, RegisteredSensor>(StringComparer.Ordinal)
        {
            [LocationSensorSource.LocationId] = new() { Type = "sensor", Name = "Location" }
        };
        var client = new FakeClient();
        var catalog = LocationPlusBatteryCatalog(locProvider);
        catalog.Start(() => { });
        await catalog.RefreshAsync();
        var svc = new SensorSyncService(client, catalog, registered);

        await svc.SyncAsync("wh", SensorReadContext.Periodic);

        // The legacy sensor entry should be retired (disabled).
        var retire = Assert.Single(client.RegisterCalls, c => c.Id == LocationSensorSource.LocationId);
        Assert.True(retire.Disabled);
        Assert.False(registered.ContainsKey(LocationSensorSource.LocationId));

        catalog.Stop();
    }
}
