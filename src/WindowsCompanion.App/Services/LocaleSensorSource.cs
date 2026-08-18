using System.Globalization;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;
using Microsoft.Win32;

namespace WindowsCompanion_App.Services;

/// <summary>
/// Reports the user's regional format (a BCP 47 name such as <c>nl-NL</c>) and
/// the PC's time zone.
/// </summary>
/// <remarks>
/// Windows has four separate "locale" concepts: display language, regional
/// format, country/region, and keyboard layout. The sensor reports the
/// <em>regional format</em> - the setting that decides date, time and number
/// presentation - because that is what an automation formatting output for this
/// PC actually needs. The display language and region are attributes, so no
/// second entity is needed to answer the other questions.
///
/// Both values are read live from documented user settings. System events cover
/// setting changes, while a one-shot schedule covers automatic UTC-offset
/// transitions without polling.
/// </remarks>
public sealed class LocaleSensorSource : ISensorSource
{
    private static readonly TimeSpan MaxTransitionDelay = TimeSpan.FromDays(30);
    private static readonly TimeSpan TransitionWakeTolerance = TimeSpan.FromSeconds(1);

    public const string LocaleId = "locale";
    public const string TimeZoneId = "time_zone";

    private const string InternationalKey = @"Control Panel\International";

    private readonly ChangeGate<(string Locale, string TimeZone, int? UtcOffsetSeconds)> _state =
        new((LocaleFormatter.Unknown, LocaleFormatter.Unknown, null));
    private readonly object _lifecycleGate = new();

    private Action? _onChanged;
    private CancellationTokenSource? _transitionCancellation;
    private bool _monitorOffsetChanges;
    private bool _observing;

    public IReadOnlyList<SensorDefinition> Definitions { get; } =
    [
        new(
            LocaleId,
            "Locale",
            "The Windows regional format for this user, such as nl-NL.",
            SensorPrivacy.Benign,
            EnabledByDefault: true,
            ResourceUsage: "Low. Does not check repeatedly. Reads this PC's regional settings only "
                           + "when Windows reports a change."),
        new(
            TimeZoneId,
            "Time Zone",
            "The time zone this PC is set to, preferring the IANA name Home Assistant uses.",
            SensorPrivacy.Benign,
            EnabledByDefault: true,
            ResourceUsage: "Low. Schedules one wake-up for the next UTC-offset transition, "
                           + "listens for settings changes, and does not use the internet.",
            AutomationIdea: "When the time zone changes away from home, enable travel mode.")
    ];

    public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context)
    {
        if (!string.Equals(context.Reason, "Preview", StringComparison.Ordinal))
            SetOffsetMonitoringEnabled(enabled.Contains(TimeZoneId));
        if (!enabled.Contains(LocaleId) && !enabled.Contains(TimeZoneId)) return [];

        var current = Query();
        _state.Seed(current);

        var readings = new List<Sensor>();

        if (enabled.Contains(LocaleId))
        {
            readings.Add(new Sensor
            {
                UniqueId = LocaleId,
                Type = "sensor",
                Name = "Locale",
                State = current.Locale,
                EntityCategory = "diagnostic",
                Icon = "mdi:web",
                Attributes = BuildLocaleAttributes()
            });
        }

        if (enabled.Contains(TimeZoneId))
        {
            readings.Add(new Sensor
            {
                UniqueId = TimeZoneId,
                Type = "sensor",
                Name = "Time Zone",
                State = current.TimeZone,
                EntityCategory = "diagnostic",
                Icon = "mdi:map-clock",
                Attributes = BuildTimeZoneAttributes(current.UtcOffsetSeconds)
            });
        }

        return readings;
    }

    public void Start(Action onChanged)
    {
        CancellationTokenSource? current;
        CancellationToken? currentToken;
        lock (_lifecycleGate)
        {
            _onChanged = onChanged;
            if (_observing) return;

            _state.Seed(Query());
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemEvents.TimeChanged += OnTimeChanged;
            _observing = true;
            (current, currentToken) = ReplaceOffsetTransitionMonitorLocked();
        }

        ActivateOffsetTransitionMonitor(current, currentToken);
    }

    public void Stop()
    {
        lock (_lifecycleGate)
        {
            if (!_observing) return;

            _observing = false;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SystemEvents.TimeChanged -= OnTimeChanged;
            _transitionCancellation?.Cancel();
            _transitionCancellation = null;
            _monitorOffsetChanges = false;
            _onChanged = null;
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.Locale or UserPreferenceCategory.General)) return;

        // The framework caches the culture per process; a regional-format change
        // is invisible until that cache is dropped.
        CultureInfo.CurrentCulture.ClearCachedData();
        Publish();
        RestartOffsetTransitionMonitor();
    }

    private void OnTimeChanged(object? sender, EventArgs e)
    {
        TimeZoneInfo.ClearCachedData();
        Publish();
        RestartOffsetTransitionMonitor();
    }

    private void Publish()
    {
        var current = Query();
        lock (_lifecycleGate)
        {
            if (!_observing) return;
            if (_state.TryUpdate(current)) _onChanged?.Invoke();
        }
    }

    private void SetOffsetMonitoringEnabled(bool enabled)
    {
        CancellationTokenSource? current;
        CancellationToken? currentToken;
        lock (_lifecycleGate)
        {
            if (_monitorOffsetChanges == enabled) return;

            _monitorOffsetChanges = enabled;
            (current, currentToken) = ReplaceOffsetTransitionMonitorLocked();
        }

        ActivateOffsetTransitionMonitor(current, currentToken);
    }

    private void RestartOffsetTransitionMonitor()
    {
        CancellationTokenSource? current;
        CancellationToken? currentToken;
        lock (_lifecycleGate)
        {
            (current, currentToken) = ReplaceOffsetTransitionMonitorLocked();
        }

        ActivateOffsetTransitionMonitor(current, currentToken);
    }

    private (CancellationTokenSource? Current, CancellationToken? CurrentToken)
        ReplaceOffsetTransitionMonitorLocked()
    {
        _transitionCancellation?.Cancel();
        var current = _observing && _monitorOffsetChanges
            ? new CancellationTokenSource()
            : null;
        _transitionCancellation = current;
        return (current, current?.Token);
    }

    private void ActivateOffsetTransitionMonitor(
        CancellationTokenSource? current,
        CancellationToken? currentToken)
    {
        if (current is not null && currentToken is { } token)
            _ = MonitorOffsetTransitionsAsync(current, token);
    }

    private async Task MonitorOffsetTransitionsAsync(
        CancellationTokenSource cancellation,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var nextChange = LocaleFormatter.NextUtcOffsetChange(
                    TimeZoneInfo.Local,
                    DateTimeOffset.UtcNow);
                if (nextChange is null) return;

                await DelayUntilAsync(
                    nextChange.Value + TransitionWakeTolerance,
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                Publish();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }
        finally
        {
            lock (_lifecycleGate)
            {
                if (ReferenceEquals(_transitionCancellation, cancellation))
                    _transitionCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    private static async Task DelayUntilAsync(
        DateTimeOffset target,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var remaining = target - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) return;

            await Task.Delay(
                remaining < MaxTransitionDelay ? remaining : MaxTransitionDelay,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static (string Locale, string TimeZone, int? UtcOffsetSeconds) Query()
    {
        var locale = DescribeLocale();
        try
        {
            var local = TimeZoneInfo.Local;
            var now = DateTimeOffset.Now;
            return (locale, DescribeTimeZone(local), LocaleFormatter.UtcOffsetSeconds(local, now));
        }
        catch (TimeZoneNotFoundException)
        {
            return (locale, LocaleFormatter.Unknown, null);
        }
        catch (InvalidTimeZoneException)
        {
            return (locale, LocaleFormatter.Unknown, null);
        }
    }

    /// <summary>
    /// Prefers the live user setting from the registry, because .NET caches the
    /// process culture and a running app would otherwise report a stale locale
    /// until it restarted.
    /// </summary>
    private static string DescribeLocale()
    {
        var fromRegistry = LocaleFormatter.Describe(ReadInternational("LocaleName"));
        return fromRegistry != LocaleFormatter.Unknown
            ? fromRegistry
            : LocaleFormatter.Describe(CultureInfo.CurrentCulture.Name);
    }

    private static string DescribeTimeZone(TimeZoneInfo local)
    {
        var iana = local.HasIanaId
            ? local.Id
            : TimeZoneInfo.TryConvertWindowsIdToIanaId(local.Id, out var converted)
                ? converted
                : null;

        return LocaleFormatter.DescribeTimeZone(iana, local.Id);
    }

    private static IDictionary<string, object> BuildLocaleAttributes()
    {
        var attributes = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["display_language"] = LocaleFormatter.Describe(CultureInfo.InstalledUICulture.Name)
        };

        try
        {
            attributes["region"] = RegionInfo.CurrentRegion.TwoLetterISORegionName;
        }
        catch (ArgumentException)
        {
            // An invariant or unusual culture has no region; the locale still stands.
        }

        return attributes;
    }

    private static IDictionary<string, object>? BuildTimeZoneAttributes(int? utcOffsetSeconds) =>
        utcOffsetSeconds is { } offset
            ? LocaleFormatter.BuildTimeZoneAttributes(offset)
            : null;

    private static string? ReadInternational(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternationalKey);
            return key?.GetValue(name) as string;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
                                       or System.Security.SecurityException
                                       or IOException)
        {
            return null;
        }
    }
}
