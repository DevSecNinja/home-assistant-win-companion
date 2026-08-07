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
            return Task.CompletedTask;
        }

        public Task UpdateSensorsAsync(string webhookId, IReadOnlyList<Sensor> sensors, CancellationToken ct = default)
        {
            Updates++;
            if (FailNextUpdate)
            {
                FailNextUpdate = false;
                throw new HttpRequestException("boom");
            }
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Sync_registers_each_sensor_once_then_updates()
    {
        var client = new FakeClient();
        var svc = new SensorSyncService(client, new FakeStatus());

        await svc.SyncAsync("wh");
        await svc.SyncAsync("wh");

        // Two unique sensors registered exactly once each across two syncs.
        Assert.Equal(2, client.Registered.Count);
        Assert.Equal(2, client.Updates);
    }

    [Fact]
    public async Task Failed_update_forces_reregistration_on_next_sync()
    {
        var client = new FakeClient();
        var svc = new SensorSyncService(client, new FakeStatus());

        await svc.SyncAsync("wh");                 // registers 2, update ok
        client.FailNextUpdate = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => svc.SyncAsync("wh"));
        await svc.SyncAsync("wh");                 // must re-register the 2 sensors

        Assert.Equal(4, client.Registered.Count);  // 2 + 2 after the forced reset
    }
}
