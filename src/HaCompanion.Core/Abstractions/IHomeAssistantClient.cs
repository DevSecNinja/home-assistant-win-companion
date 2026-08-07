using HaCompanion.Core.Models;

namespace HaCompanion.Core.Abstractions;

/// <summary>Talks to a Home Assistant instance (REST + webhook).</summary>
public interface IHomeAssistantClient
{
    /// <summary>Validates connectivity and the access token (GET /api/).</summary>
    Task<bool> ValidateAsync(CancellationToken ct = default);

    /// <summary>Registers this device with the mobile_app integration.</summary>
    Task<DeviceRegistrationResponse> RegisterDeviceAsync(DeviceRegistrationRequest request, CancellationToken ct = default);

    /// <summary>Registers a single sensor (must be done before updating it).</summary>
    Task RegisterSensorAsync(string webhookId, Sensor sensor, CancellationToken ct = default);

    /// <summary>Sends a batch state update for already-registered sensors.</summary>
    Task UpdateSensorsAsync(string webhookId, IReadOnlyList<Sensor> sensors, CancellationToken ct = default);
}
