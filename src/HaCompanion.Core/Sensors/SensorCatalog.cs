using HaCompanion.Core.Models;

namespace HaCompanion.Core.Sensors;

/// <summary>
/// The set of sensors this installation can report, plus the user's choices about
/// which of them actually are. The catalog owns starting and stopping the
/// underlying <see cref="ISensorSource"/>s so that a sensor switched off costs
/// nothing: its OS hook is released rather than its value merely being discarded.
/// </summary>
public sealed class SensorCatalog
{
    private readonly IReadOnlyList<ISensorSource> _sources;
    private readonly SensorPreferences _preferences;
    private readonly HashSet<ISensorSource> _running = new();
    private Action? _onChanged;
    private bool _started;

    public SensorCatalog(IEnumerable<ISensorSource> sources, SensorPreferences preferences)
    {
        _sources = (sources ?? throw new ArgumentNullException(nameof(sources))).ToList();
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
    }

    public SensorPreferences Preferences => _preferences;

    public IReadOnlyList<SensorDefinition> Definitions =>
        _sources.SelectMany(s => s.Definitions).ToList();

    public bool IsEnabled(string uniqueId)
    {
        var definition = Definitions.FirstOrDefault(d => d.UniqueId == uniqueId);
        return definition is not null && _preferences.IsEnabled(definition);
    }

    /// <summary>Ids of every currently enabled sensor.</summary>
    public IReadOnlySet<string> EnabledIds =>
        Definitions.Where(_preferences.IsEnabled).Select(d => d.UniqueId).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Begins observing. <paramref name="onChanged"/> is invoked when a source
    /// detects a state change and an immediate push is warranted.
    /// </summary>
    public void Start(Action onChanged)
    {
        _onChanged = onChanged;
        _started = true;
        SyncRunningSources();
    }

    public void Stop()
    {
        _started = false;
        foreach (var source in _running.ToList())
        {
            source.Stop();
            _running.Remove(source);
        }
    }

    /// <summary>Changes a sensor's enablement and starts/stops its source to match.</summary>
    public void SetEnabled(string uniqueId, bool enabled)
    {
        _preferences.Set(uniqueId, enabled);
        SyncRunningSources();
    }

    /// <summary>Collects readings for every enabled sensor.</summary>
    public IReadOnlyList<Sensor> Read(SensorReadContext context)
    {
        var enabled = EnabledIds;
        var readings = new List<Sensor>();

        foreach (var source in _sources)
        {
            if (!source.Definitions.Any(d => enabled.Contains(d.UniqueId))) continue;
            readings.AddRange(source.Read(enabled, context));
        }

        foreach (var reading in readings)
            Truncate(reading);

        return readings;
    }

    /// <summary>
    /// Reads every sensor regardless of whether it is enabled, so the UI can show
    /// the user exactly what a sensor would report before they switch it on.
    /// Purely local: nothing produced here is transmitted.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> PreviewAsync(
        CancellationToken cancellationToken = default)
    {
        var all = Definitions.Select(d => d.UniqueId).ToHashSet(StringComparer.Ordinal);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var source in _sources)
        {
            IReadOnlyList<Sensor> readings;
            try
            {
                readings = await source.PreviewAsync(all, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                continue; // A preview must never break the settings UI.
            }

            foreach (var reading in readings)
                values[reading.UniqueId] = Describe(reading.State);
        }

        return values;
    }

    private static string Describe(object? state) => state switch
    {
        null => "Unavailable",
        bool b => b ? "Yes" : "No",
        _ => state.ToString() ?? "Unavailable"
    };

    /// <summary>Home Assistant rejects states longer than 255 characters.</summary>
    private static void Truncate(Sensor sensor)
    {
        if (sensor.State is string text && text.Length > 255)
            sensor.State = text[..255];
    }

    private void SyncRunningSources()
    {
        if (!_started) return;
        var enabled = EnabledIds;

        foreach (var source in _sources)
        {
            var wanted = source.Definitions.Any(d => enabled.Contains(d.UniqueId));
            var running = _running.Contains(source);

            if (wanted && !running)
            {
                source.Start(() => _onChanged?.Invoke());
                _running.Add(source);
            }
            else if (!wanted && running)
            {
                source.Stop();
                _running.Remove(source);
            }
        }
    }
}
