using HaCompanion.Core.Abstractions;
using HaCompanion.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HaCompanion.Core.Sensors;

/// <summary>
/// Registers the machine sensors once and pushes periodic state updates to
/// Home Assistant. Registration is idempotent per unique_id.
/// </summary>
public sealed class SensorSyncService
{
    private readonly IHomeAssistantClient _client;
    private readonly ISystemStatusProvider _status;
    private readonly ILogger<SensorSyncService> _log;
    private readonly HashSet<string> _registered = new(StringComparer.Ordinal);

    public SensorSyncService(
        IHomeAssistantClient client,
        ISystemStatusProvider status,
        ILogger<SensorSyncService>? log = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _log = log ?? NullLogger<SensorSyncService>.Instance;
    }

    /// <summary>Registers all sensors that have not yet been registered.</summary>
    public async Task EnsureRegisteredAsync(string webhookId, CancellationToken ct = default)
    {
        foreach (var sensor in BatterySensorProvider.BuildAll(_status.GetStatus()))
        {
            if (_registered.Contains(sensor.UniqueId)) continue;
            await _client.RegisterSensorAsync(webhookId, sensor, ct).ConfigureAwait(false);
            _registered.Add(sensor.UniqueId);
        }
    }

    /// <summary>Registers (if needed) then pushes the latest sensor states.</summary>
    public async Task SyncAsync(string webhookId, CancellationToken ct = default)
    {
        await EnsureRegisteredAsync(webhookId, ct).ConfigureAwait(false);
        var sensors = BatterySensorProvider.BuildAll(_status.GetStatus());
        try
        {
            await _client.UpdateSensorsAsync(webhookId, sensors, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A restarted HA may have forgotten our sensors; force re-registration next cycle.
            _log.LogWarning(ex, "Sensor update failed; will re-register on next sync.");
            _registered.Clear();
            throw;
        }
    }
}
