using WindowsCompanion.Core.Abstractions;
using WindowsCompanion.Core.HomeAssistant;
using WindowsCompanion.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WindowsCompanion.Core.Sensors;

/// <summary>
/// Registers enabled sensors and pushes their states to Home Assistant.
/// </summary>
/// <remarks>
/// Enabling and disabling both go through <c>register_sensor</c>, not
/// <c>update_sensor_states</c>: Home Assistant only honours the <c>disabled</c> flag
/// on the re-registration path, so a disable sent in an update batch is silently
/// ignored. For the same reason the flag is always sent explicitly - omitting it
/// leaves the previous value untouched, which would make a re-enabled sensor stay
/// disabled forever.
///
/// The set of registered sensors is persisted by the caller. That is what lets a
/// sensor removed from the app in a later version be retired: on the next sync it is
/// registered but no longer produced, so it takes the same path as a user-disabled
/// sensor. Without persistence it would simply be forgotten, and Home Assistant would
/// keep showing its last value forever.
/// </remarks>
public sealed class SensorSyncService
{
    private readonly IHomeAssistantClient _client;
    private readonly SensorCatalog _catalog;
    private readonly IDictionary<string, RegisteredSensor> _registered;
    private readonly Action? _persist;
    private readonly ILogger<SensorSyncService> _log;

    /// <summary>
    /// Serialises syncs. The periodic loop and change-driven pushes (idle timer,
    /// session and power events, settings changes) all land here from different
    /// threads and mutate <see cref="_registered"/>. Without this, concurrent syncs
    /// corrupt it or throw "collection was modified" - which the caller logs as a
    /// transient failure, so it reads like a flaky network rather than a bug.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SensorSyncService(
        IHomeAssistantClient client,
        SensorCatalog catalog,
        IDictionary<string, RegisteredSensor>? registered = null,
        Action? persist = null,
        ILogger<SensorSyncService>? log = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _registered = registered ?? new Dictionary<string, RegisteredSensor>(StringComparer.Ordinal);
        _persist = persist;
        _log = log ?? NullLogger<SensorSyncService>.Instance;
    }

    /// <summary>Registers (if needed) then pushes the latest sensor states.</summary>
    public async Task SyncAsync(string webhookId, SensorReadContext context, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await SyncCoreAsync(webhookId, context, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SyncCoreAsync(string webhookId, SensorReadContext context, CancellationToken ct)
    {
        var readings = _catalog.Read(context);

        // Location is sent via update_location (device tracker), not as a regular sensor.
        var locationReading = readings.FirstOrDefault(s =>
            string.Equals(s.UniqueId, LocationSensorSource.LocationId, StringComparison.Ordinal));
        var sensorReadings = locationReading is null
            ? readings
            : readings.Where(s => s != locationReading).ToList();

        var changed = false;

        foreach (var sensor in sensorReadings)
        {
            if (_registered.ContainsKey(sensor.UniqueId)) continue;

            // Explicitly enable: a sensor the user previously switched off is still
            // flagged disabled in Home Assistant until we say otherwise.
            sensor.Disabled = false;
            await _client.RegisterSensorAsync(webhookId, sensor, ct).ConfigureAwait(false);
            sensor.Disabled = null;

            _registered[sensor.UniqueId] = new RegisteredSensor { Type = sensor.Type, Name = sensor.Name };
            changed = true;
        }

        // Anything Home Assistant knows about that we no longer produce: either the
        // user switched it off, or it was removed from the app entirely.
        var live = sensorReadings.Select(r => r.UniqueId).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _registered.Keys.Where(id => !live.Contains(id)).ToList())
        {
            var known = _registered[id];
            await _client.RegisterSensorAsync(webhookId, new Sensor
            {
                UniqueId = id,
                Type = known.Type,
                Name = known.Name,
                Disabled = true
            }, ct).ConfigureAwait(false);

            _registered.Remove(id);
            changed = true;
            _log.LogInformation("Retired sensor {UniqueId} in Home Assistant.", id);
        }

        if (changed) _persist?.Invoke();

        if (sensorReadings.Count > 0)
        {
            try
            {
                await _client.UpdateSensorsAsync(webhookId, sensorReadings, ct).ConfigureAwait(false);
            }
            catch (HomeAssistantRejectedException ex)
            {
                if (ex.SensorsUnregistered)
                {
                    _log.LogWarning(ex, "Home Assistant no longer knows these sensors; re-registering.");
                    _registered.Clear();
                    _persist?.Invoke();
                }

                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogDebug("Sensor update failed; registrations will be refreshed on the next sync "
                              + "({ErrorType}).", ex.GetType().Name);
                _registered.Clear();
                _persist?.Invoke();
                throw;
            }
        }

        // Send location via update_location so Home Assistant updates the device
        // tracker entity (shows zone names on the map, not raw coordinates).
        if (locationReading is not null)
        {
            Models.LocationUpdate locationUpdate;
            if (locationReading.Attributes is { } attrs
                && attrs.TryGetValue("latitude", out var latObj) && latObj is double lat
                && attrs.TryGetValue("longitude", out var lngObj) && lngObj is double lng
                && attrs.TryGetValue("gps_accuracy", out var accObj) && accObj is double acc)
            {
                locationUpdate = new Models.LocationUpdate(lat, lng, (int)acc);
            }
            else
            {
                // No fix (permission denied or unavailable): clear GPS so HA does
                // not keep showing the last stale position (FR-005).
                locationUpdate = new Models.LocationUpdate(null, null, null, "not_home");
            }

            await _client.UpdateLocationAsync(webhookId, locationUpdate, ct).ConfigureAwait(false);
        }
    }
}
