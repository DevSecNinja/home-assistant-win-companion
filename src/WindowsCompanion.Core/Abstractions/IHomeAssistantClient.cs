using WindowsCompanion.Core.Models;

namespace WindowsCompanion.Core.Abstractions;

/// <summary>Talks to a Home Assistant instance (REST + webhook).</summary>
public interface IHomeAssistantClient
{
    /// <summary>Validates connectivity and the access token (GET /api/).</summary>
    Task<bool> ValidateAsync(CancellationToken ct = default);

    /// <summary>Registers this device with the mobile_app integration.</summary>
    Task<DeviceRegistrationResponse> RegisterDeviceAsync(DeviceRegistrationRequest request, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing registration's app_data (used to declare local push
    /// support on instances where this device registered before that was added).
    /// </summary>
    Task UpdateRegistrationAsync(string webhookId, DeviceRegistrationRequest request, CancellationToken ct = default);

    /// <summary>Registers a single sensor (must be done before updating it).</summary>
    Task RegisterSensorAsync(string webhookId, Sensor sensor, CancellationToken ct = default);

    /// <summary>Sends a batch state update for already-registered sensors.</summary>
    Task UpdateSensorsAsync(string webhookId, IReadOnlyList<Sensor> sensors, CancellationToken ct = default);

    /// <summary>
    /// Updates the device tracker location via the <c>update_location</c> webhook command.
    /// This is the proper mobile_app mechanism for reporting device location on the map,
    /// enabling zone-based state (e.g. "Home") rather than raw coordinates.
    /// </summary>
    Task UpdateLocationAsync(string webhookId, LocationUpdate location, CancellationToken ct = default);

    /// <summary>
    /// Asks the instance behind this address to describe itself through the
    /// existing webhook (<c>get_config</c>). Returns null when the webhook is not
    /// known there, which is how a different instance is detected. Never creates
    /// or changes a registration.
    /// </summary>
    Task<HaInstanceInfo?> GetInstanceInfoAsync(string webhookId, CancellationToken ct = default);

    /// <summary>Reads the instance's own internal/external URLs (GET /api/config).</summary>
    Task<HaConfigInfo?> GetConfigAsync(CancellationToken ct = default);
}
