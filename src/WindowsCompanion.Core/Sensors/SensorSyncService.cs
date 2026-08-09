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
        var changed = false;

        foreach (var sensor in readings)
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
        var live = readings.Select(r => r.UniqueId).ToHashSet(StringComparer.Ordinal);
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

        if (readings.Count == 0) return;

        try
        {
            await _client.UpdateSensorsAsync(webhookId, readings, ct).ConfigureAwait(false);
        }
        catch (HomeAssistantRejectedException ex)
        {
            // "not_registered" means Home Assistant has forgotten a sensor, usually
            // because the entity was deleted there. Clearing local state lets the
            // next sync re-register it; without this it would send updates for a
            // sensor HA does not know about, forever.
            if (ex.SensorsUnregistered)
            {
                _log.LogWarning(ex, "Home Assistant no longer knows these sensors; re-registering.");
                _registered.Clear();
                _persist?.Invoke();
            }

            // A format rejection is a bug in what we send, not stale registration:
            // re-registering would loop forever. Surface it and leave state alone.
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A restarted HA may have forgotten our sensors; force re-registration next cycle.
            _log.LogWarning(ex, "Sensor update failed; will re-register on next sync.");
            _registered.Clear();
            _persist?.Invoke();
            throw;
        }
    }
}
