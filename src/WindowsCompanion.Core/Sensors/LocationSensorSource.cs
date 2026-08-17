using System.Globalization;
using WindowsCompanion.Core.Abstractions;
using WindowsCompanion.Core.Models;

namespace WindowsCompanion.Core.Sensors;

public sealed class LocationSensorSource : ISensorSource, IRefreshableSensorSource
{
    public const string LocationId = "location";

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);

    private readonly ILocationProvider _provider;
    private readonly SensorPreferences _preferences;
    private readonly SensorPollLoop _loop;
    private readonly object _gate = new();

    /// <summary>
    /// What Home Assistant has already been told, rounded to ~4 decimal places
    /// (roughly 11 m) so GPS jitter that would not move the reported state does
    /// not trigger an extra push.
    /// </summary>
    private readonly ChangeGate<(LocationStatus Status, double Lat, double Lng)> _published =
        new((LocationStatus.Unavailable, 0, 0), HasMeaningfullyChanged);

    private LocationResult _result = LocationResult.Unavailable();
    private Action? _onChanged;

    public LocationSensorSource(
        ILocationProvider provider,
        SensorPreferences preferences,
        TimeSpan? refreshInterval = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _loop = new SensorPollLoop(CheckAsync, refreshInterval ?? RefreshInterval);
    }

    public IReadOnlyList<SensorDefinition> Definitions { get; } =
    [
        new(
            LocationId,
            "Location",
            "This device's current latitude and longitude, from Windows Location Services.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false,
            ResourceUsage: "Low. Reads the current position when enabled and every 15 minutes. "
                           + "Sends an extra update only when the position meaningfully changes.",
            AutomationIdea: "The device tracker entity created by this sensor enables "
                            + "zone-based automations (arrival/departure) directly in Home "
                            + "Assistant without additional templates.",
            OptInPlaceholder: "Enable to read this device's location")
    ];

    public IReadOnlyList<Sensor> Read(
        IReadOnlySet<string> enabled, SensorReadContext context)
    {
        if (!enabled.Contains(LocationId)) return [];

        return [BuildSensor(CurrentResult())];
    }

    public ValueTask<IReadOnlyList<Sensor>> PreviewAsync(
        IReadOnlySet<string> requested,
        CancellationToken cancellationToken = default)
    {
        var permitted = SensorPreviewGate.Permitted(Definitions, requested, _preferences);
        var readings = Read(permitted, new SensorReadContext("Preview")).ToList();

        var definition = Definitions[0];
        if (requested.Contains(LocationId) && !permitted.Contains(LocationId))
        {
            readings.Add(new Sensor
            {
                UniqueId = LocationId,
                Name = definition.Name,
                State = definition.DisabledPreview
            });
        }

        return ValueTask.FromResult<IReadOnlyList<Sensor>>(readings);
    }

    public void Start(Action onChanged)
    {
        _onChanged = onChanged;
        _loop.Start();
    }

    public void Stop() => _loop.Stop();

    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        _loop.RunOnceAsync(cancellationToken);

    private async Task CheckAsync(SensorPollReason reason, CancellationToken cancellationToken)
    {
        var current = await _provider
            .GetLocationAsync(cancellationToken)
            .ConfigureAwait(false);

        lock (_gate) _result = current;

        // A manual refresh is followed by a push anyway, and a scheduled poll
        // that lands within GPS jitter of the last published position is not
        // worth waking the sync for.
        if (reason == SensorPollReason.Scheduled
            && _published.TryUpdate((current.Status, current.Latitude ?? 0, current.Longitude ?? 0)))
        {
            _onChanged?.Invoke();
        }
    }

    private LocationResult CurrentResult()
    {
        lock (_gate) return _result;
    }

    private static Sensor BuildSensor(LocationResult result) =>
        result.Status == LocationStatus.Ready
            ? new Sensor
            {
                UniqueId = LocationId,
                Type = "sensor",
                Name = "Location",
                State = $"{result.Latitude!.Value.ToString("F6", CultureInfo.InvariantCulture)},"
                        + $"{result.Longitude!.Value.ToString("F6", CultureInfo.InvariantCulture)}",
                Attributes = new Dictionary<string, object>
                {
                    ["latitude"] = result.Latitude!.Value,
                    ["longitude"] = result.Longitude!.Value,
                    ["gps_accuracy"] = result.AccuracyMeters!.Value
                },
                Icon = "mdi:crosshairs-gps"
            }
            : new Sensor
            {
                UniqueId = LocationId,
                Type = "sensor",
                Name = "Location",
                State = result.Status == LocationStatus.PermissionDenied
                    ? "Location permission required"
                    : "Unavailable",
                Icon = "mdi:crosshairs-question"
            };

    /// <summary>
    /// A status change is always news; a position change only counts once it
    /// moves by more than ~0.0001 degrees (~11 m) in either coordinate.
    /// </summary>
    private static bool HasMeaningfullyChanged(
        (LocationStatus Status, double Lat, double Lng) previous,
        (LocationStatus Status, double Lat, double Lng) current) =>
        previous.Status != current.Status
        || Math.Abs(previous.Lat - current.Lat) > 0.0001
        || Math.Abs(previous.Lng - current.Lng) > 0.0001;
}
