using HaCompanion.Core.Abstractions;
using HaCompanion.Core.Models;
using HaCompanion.Core.Sensors;
using Xunit;

namespace HaCompanion.Core.Tests;

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
        public int Updates;
        public bool FailNextUpdate;

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
            return Task.CompletedTask;
        }
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

        // Two unique sensors registered exactly once each across two syncs.
        Assert.Equal(2, client.Registered.Count);
        Assert.Equal(2, client.Updates);
    }

    [Fact]
    public async Task Failed_update_forces_reregistration_on_next_sync()
    {
        var client = new FakeClient();
        var svc = new SensorSyncService(client, BatteryCatalog());

        await svc.SyncAsync("wh", SensorReadContext.Periodic);                 // registers 2, update ok
        client.FailNextUpdate = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => svc.SyncAsync("wh", SensorReadContext.Periodic));
        await svc.SyncAsync("wh", SensorReadContext.Periodic);                 // must re-register the 2 sensors

        Assert.Equal(4, client.Registered.Count);  // 2 + 2 after the forced reset
    }

    [Fact]
    public async Task Disabled_sensors_are_not_registered_or_sent()
    {
        var prefs = new SensorPreferences();
        prefs.Set(BatterySensorProvider.BatteryStateId, false);

        var client = new FakeClient();
        var svc = new SensorSyncService(client, BatteryCatalog(prefs));

        await svc.SyncAsync("wh", SensorReadContext.Periodic);

        Assert.Equal(new[] { BatterySensorProvider.BatteryLevelId }, client.Registered);
        var batch = Assert.Single(client.Batches);
        Assert.Equal(BatterySensorProvider.BatteryLevelId, Assert.Single(batch).UniqueId);
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
            new[] { BatterySensorProvider.BatteryLevelId, BatterySensorProvider.BatteryStateId },
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
}

