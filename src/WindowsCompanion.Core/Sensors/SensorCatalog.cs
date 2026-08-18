using WindowsCompanion.Core.Models;

namespace WindowsCompanion.Core.Sensors;

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
    private readonly object _lifetime = new();
    private readonly SemaphoreSlim _preview = new(1, 1);
    private readonly Dictionary<ISensorSource, int> _changeNotificationSuppression = new();
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
        lock (_lifetime)
        {
            _onChanged = onChanged;
            _started = true;
        }

        SyncRunningSources();
    }

    public void Stop()
    {
        List<ISensorSource> running;
        lock (_lifetime)
        {
            _started = false;
            running = _running.ToList();
            _running.Clear();
        }

        // Cleared before stopping, so a hook that fires while it is being
        // released finds itself unregistered and is dropped rather than pushed
        // over a connection that is already going away.
        foreach (var source in running)
            source.Stop();
    }

    /// <summary>Changes a sensor's enablement and starts/stops its source to match.</summary>
    public void SetEnabled(string uniqueId, bool enabled)
    {
        _preferences.Set(uniqueId, enabled);
        SyncRunningSources();
    }

    /// <summary>
    /// Applies an enablement change and obtains the source's first fresh snapshot
    /// without letting its startup callback race the caller's explicit settings sync.
    /// </summary>
    public async Task<string?> SetEnabledAndRefreshAsync(
        string uniqueId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var source = SourceFor(uniqueId);
        var definition = source.Definitions.First(candidate =>
            string.Equals(candidate.UniqueId, uniqueId, StringComparison.Ordinal));
        var wasEnabled = _preferences.IsEnabled(definition);
        lock (_lifetime)
        {
            _changeNotificationSuppression.TryGetValue(source, out var count);
            _changeNotificationSuppression[source] = count + 1;
        }

        try
        {
            try
            {
                SetEnabled(uniqueId, enabled);
            }
            catch (Exception enableFailure)
            {
                try
                {
                    if (_preferences.IsEnabled(definition) != wasEnabled)
                        SetEnabled(uniqueId, wasEnabled);
                }
                catch (Exception rollbackFailure)
                {
                    throw new AggregateException(
                        $"Could not change sensor '{uniqueId}' or restore its previous state.",
                        enableFailure,
                        rollbackFailure);
                }

                throw;
            }

            if (!enabled && definition.Privacy == SensorPrivacy.Sensitive)
                return definition.DisabledPreview;

            var requested = new HashSet<string>(StringComparer.Ordinal) { uniqueId };
            await _preview.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                IReadOnlyList<Sensor> readings;
                if (source is IRefreshableSensorSource refreshable)
                {
                    if (enabled)
                        await refreshable.RefreshAsync(cancellationToken).ConfigureAwait(false);
                    readings = source.Read(requested, new SensorReadContext("Preview"));
                }
                else
                {
                    readings = await source
                        .PreviewAsync(requested, cancellationToken)
                        .ConfigureAwait(false);
                }

                return DescribeReading(readings, uniqueId);
            }
            finally
            {
                _preview.Release();
            }
        }
        finally
        {
            lock (_lifetime)
            {
                var count = _changeNotificationSuppression[source] - 1;
                if (count == 0)
                    _changeNotificationSuppression.Remove(source);
                else
                    _changeNotificationSuppression[source] = count;
            }
        }
    }

    /// <summary>Refreshes expensive enabled sources before an explicit user push.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var enabled = EnabledIds;
        foreach (var source in _sources)
        {
            if (source is not IRefreshableSensorSource refreshable
                || !source.Definitions.Any(definition => enabled.Contains(definition.UniqueId)))
            {
                continue;
            }

            await refreshable.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Refreshes the enabled source that owns one sensor, when supported.</summary>
    public async Task RefreshSensorAsync(
        string uniqueId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uniqueId);

        var source = SourceFor(uniqueId);
        if (!IsEnabled(uniqueId) || source is not IRefreshableSensorSource refreshable) return;

        await refreshable.RefreshAsync(cancellationToken).ConfigureAwait(false);
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
    /// Describes cached readings for enabled sensors without triggering source collection.
    /// Intended for live UI updates while source-owned hooks and poll loops keep snapshots current.
    /// </summary>
    public IReadOnlyDictionary<string, string> PreviewEnabled()
    {
        var enabled = EnabledIds;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var source in _sources)
        {
            var requested = source.Definitions
                .Where(definition => enabled.Contains(definition.UniqueId))
                .Select(definition => definition.UniqueId)
                .ToHashSet(StringComparer.Ordinal);
            if (requested.Count == 0) continue;

            IReadOnlyList<Sensor> readings;
            try
            {
                readings = source.Read(requested, new SensorReadContext("LivePreview"));
            }
            catch (Exception)
            {
                continue; // One cached preview must not break the settings UI.
            }

            foreach (var reading in readings)
                values[reading.UniqueId] = Describe(reading.State);
        }

        return values;
    }

    /// <summary>
    /// Reads every sensor the user is allowed to see, so the UI can show exactly
    /// what a sensor would report before they switch it on. Purely local: nothing
    /// produced here is transmitted.
    /// </summary>
    /// <remarks>
    /// A privacy-sensitive sensor is only read once it is enabled. Gating here
    /// rather than in each source means a new sensitive source cannot leak by
    /// forgetting to apply <see cref="SensorPreviewGate"/> itself.
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, string>> PreviewAsync(
        CancellationToken cancellationToken = default)
    {
        var definitions = Definitions;
        var all = definitions.Select(d => d.UniqueId).ToHashSet(StringComparer.Ordinal);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        await _preview.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var source in _sources)
            {
                var permitted = SensorPreviewGate.Permitted(
                    source.Definitions, all, _preferences);
                foreach (var definition in source.Definitions.Where(definition =>
                             all.Contains(definition.UniqueId)
                             && !permitted.Contains(definition.UniqueId)))
                {
                    values[definition.UniqueId] = definition.DisabledPreview;
                }

                if (permitted.Count == 0) continue;

                IReadOnlyList<Sensor> readings;
                try
                {
                    readings = await source.PreviewAsync(permitted, cancellationToken).ConfigureAwait(false);
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
        }
        finally
        {
            _preview.Release();
        }

        return values;
    }

    /// <summary>
    /// Reads one current local-only preview after a settings change. Unlike the
    /// full settings-page preview, source failures are reported to the caller so
    /// the row can show that its value did not refresh.
    /// </summary>
    public async Task<string?> PreviewSensorAsync(
        string uniqueId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uniqueId);

        var match = _sources
            .SelectMany(source => source.Definitions.Select(definition => (source, definition)))
            .FirstOrDefault(candidate =>
                string.Equals(candidate.definition.UniqueId, uniqueId, StringComparison.Ordinal));
        if (match.source is null)
            throw new ArgumentException($"Unknown sensor '{uniqueId}'.", nameof(uniqueId));

        if (match.definition.Privacy == SensorPrivacy.Sensitive
            && !_preferences.IsEnabled(match.definition))
        {
            return match.definition.DisabledPreview;
        }

        var requested = new HashSet<string>(StringComparer.Ordinal) { uniqueId };
        await _preview.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var readings = await match.source
                .PreviewAsync(requested, cancellationToken)
                .ConfigureAwait(false);
            return DescribeReading(readings, uniqueId);
        }
        finally
        {
            _preview.Release();
        }
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
        lock (_lifetime)
        {
            if (!_started) return;
        }

        var enabled = EnabledIds;

        foreach (var source in _sources)
        {
            var wanted = source.Definitions.Any(d => enabled.Contains(d.UniqueId));
            bool running;
            lock (_lifetime) running = _running.Contains(source);

            if (wanted && !running)
            {
                lock (_lifetime) _running.Add(source);
                try
                {
                    source.Start(() => OnSourceChanged(source));
                }
                catch
                {
                    lock (_lifetime) _running.Remove(source);
                    throw;
                }
            }
            else if (!wanted && running)
            {
                lock (_lifetime) _running.Remove(source);
                source.Stop();
            }
        }
    }

    /// <summary>
    /// Forwards a source's change notification only while that source is still
    /// running. OS hooks and poll loops can deliver one last callback after a
    /// stop; acting on it would push a reading for a sensor the user just
    /// switched off, or use a connection that is being torn down.
    /// </summary>
    private void OnSourceChanged(ISensorSource source)
    {
        Action? onChanged;
        lock (_lifetime)
        {
            if (!_started
                || !_running.Contains(source)
                || _changeNotificationSuppression.ContainsKey(source))
            {
                return;
            }

            onChanged = _onChanged;
        }

        onChanged?.Invoke();
    }

    private static string? DescribeReading(IReadOnlyList<Sensor> readings, string uniqueId)
    {
        var reading = readings.FirstOrDefault(candidate =>
            string.Equals(candidate.UniqueId, uniqueId, StringComparison.Ordinal));
        return reading is null ? null : Describe(reading.State);
    }

    private ISensorSource SourceFor(string uniqueId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uniqueId);

        return _sources.FirstOrDefault(candidate =>
                   candidate.Definitions.Any(definition =>
                       string.Equals(definition.UniqueId, uniqueId, StringComparison.Ordinal)))
               ?? throw new ArgumentException($"Unknown sensor '{uniqueId}'.", nameof(uniqueId));
    }
}
