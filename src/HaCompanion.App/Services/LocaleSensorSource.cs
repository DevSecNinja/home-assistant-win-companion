using System.Globalization;
using HaCompanion.Core.Models;
using HaCompanion.Core.Sensors;
using Microsoft.Win32;

namespace HaCompanion_App.Services;

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
/// Both values are read live from documented user settings; a locale or
/// time-zone change raises a system event, so there is no polling.
/// </remarks>
public sealed class LocaleSensorSource : ISensorSource
{
    public const string LocaleId = "locale";
    public const string TimeZoneId = "time_zone";

    private const string InternationalKey = @"Control Panel\International";

    private readonly ChangeGate<(string Locale, string TimeZone)> _state =
        new((LocaleFormatter.Unknown, LocaleFormatter.Unknown));

    private Action? _onChanged;
    private bool _observing;

    public IReadOnlyList<SensorDefinition> Definitions { get; } =
    [
        new(
            LocaleId,
            "Locale",
            "The Windows regional format for this user, such as nl-NL.",
            SensorPrivacy.Benign,
            EnabledByDefault: true),
        new(
            TimeZoneId,
            "Time Zone",
            "The time zone this PC is set to, preferring the IANA name Home Assistant uses.",
            SensorPrivacy.Benign,
            EnabledByDefault: true)
    ];

    public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context)
    {
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
                Icon = "mdi:map-clock"
            });
        }

        return readings;
    }

    public void Start(Action onChanged)
    {
        _onChanged = onChanged;
        if (_observing) return;

        _state.Seed(Query());
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.TimeChanged += OnTimeChanged;
        _observing = true;
    }

    public void Stop()
    {
        if (!_observing) return;

        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.TimeChanged -= OnTimeChanged;
        _observing = false;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.Locale or UserPreferenceCategory.General)) return;

        // The framework caches the culture per process; a regional-format change
        // is invisible until that cache is dropped.
        CultureInfo.CurrentCulture.ClearCachedData();
        Publish();
    }

    private void OnTimeChanged(object? sender, EventArgs e)
    {
        TimeZoneInfo.ClearCachedData();
        Publish();
    }

    private void Publish()
    {
        if (_state.TryUpdate(Query())) _onChanged?.Invoke();
    }

    private static (string Locale, string TimeZone) Query() => (DescribeLocale(), DescribeTimeZone());

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

    private static string DescribeTimeZone()
    {
        try
        {
            var local = TimeZoneInfo.Local;
            var iana = local.HasIanaId
                ? local.Id
                : TimeZoneInfo.TryConvertWindowsIdToIanaId(local.Id, out var converted)
                    ? converted
                    : null;

            return LocaleFormatter.DescribeTimeZone(iana, local.Id);
        }
        catch (TimeZoneNotFoundException)
        {
            return LocaleFormatter.Unknown;
        }
        catch (InvalidTimeZoneException)
        {
            return LocaleFormatter.Unknown;
        }
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
