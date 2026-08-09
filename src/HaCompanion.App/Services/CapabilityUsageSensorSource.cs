using System.Globalization;
using System.Security;
using HaCompanion.Core.Models;
using HaCompanion.Core.Sensors;
using Microsoft.Win32;

namespace HaCompanion_App.Services;

public sealed class CapabilityUsageSensorSource : ISensorSource
{
    public const string MicrophoneId = "microphone";
    public const string CameraId = "camera";

    private const string ConsentStore =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

    private readonly SensorPreferences _preferences;
    private readonly System.Timers.Timer _timer = new(TimeSpan.FromSeconds(10));
    private readonly object _gate = new();
    private Action? _onChanged;
    private ActivitySnapshot _last;
    private bool _observing;

    public CapabilityUsageSensorSource(SensorPreferences preferences)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _timer.AutoReset = true;
        _timer.Elapsed += (_, _) => Poll();
    }

    public IReadOnlyList<SensorDefinition> Definitions { get; } =
    [
        new(
            MicrophoneId,
            "Microphone In Use",
            "On while any application is using a microphone.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false,
            ResourceUsage: "Checks Windows' local capability history every 10 seconds and requests "
                           + "an immediate batch only when usage changes."),
        new(
            CameraId,
            "Camera In Use",
            "On while any application is using a camera.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false,
            ResourceUsage: "Checks Windows' local capability history every 10 seconds and requests "
                           + "an immediate batch only when usage changes.")
    ];

    public IReadOnlyList<Sensor> Read(
        IReadOnlySet<string> enabled, SensorReadContext context)
    {
        var snapshot = Capture(enabled);
        return Build(snapshot, enabled);
    }

    public void Start(Action onChanged)
    {
        _onChanged = onChanged;
        if (_observing) return;

        lock (_gate) _last = Capture(EnabledIds());
        _timer.Start();
        _observing = true;
    }

    public void Stop()
    {
        if (!_observing) return;
        _timer.Stop();
        _observing = false;
    }

    private void Poll()
    {
        var enabled = EnabledIds();
        var current = Capture(enabled);
        var changed = false;

        lock (_gate)
        {
            if (current != _last)
            {
                _last = current;
                changed = true;
            }
        }

        if (changed) _onChanged?.Invoke();
    }

    private HashSet<string> EnabledIds() =>
        Definitions.Where(_preferences.IsEnabled)
            .Select(definition => definition.UniqueId)
            .ToHashSet(StringComparer.Ordinal);

    private static ActivitySnapshot Capture(IReadOnlySet<string> enabled) => new(
        enabled.Contains(MicrophoneId) ? IsCapabilityActive("microphone") : null,
        enabled.Contains(CameraId) ? IsCapabilityActive("webcam") : null);

    private static IReadOnlyList<Sensor> Build(
        ActivitySnapshot snapshot, IReadOnlySet<string> enabled)
    {
        var sensors = new List<Sensor>();

        if (enabled.Contains(MicrophoneId))
        {
            sensors.Add(new Sensor
            {
                UniqueId = MicrophoneId,
                Type = "binary_sensor",
                Name = "Microphone In Use",
                State = snapshot.Microphone ?? false,
                Icon = snapshot.Microphone is true ? "mdi:microphone" : "mdi:microphone-off"
            });
        }

        if (enabled.Contains(CameraId))
        {
            sensors.Add(new Sensor
            {
                UniqueId = CameraId,
                Type = "binary_sensor",
                Name = "Camera In Use",
                State = snapshot.Camera ?? false,
                Icon = snapshot.Camera is true ? "mdi:video" : "mdi:video-off"
            });
        }

        return sensors;
    }

    private static bool IsCapabilityActive(string capability)
    {
        var stops = new List<long?>();
        Collect(RegistryHive.CurrentUser, capability, stops);
        Collect(RegistryHive.LocalMachine, capability, stops);
        return CapabilityActivity.IsActive(stops);
    }

    private static void Collect(
        RegistryHive hive, string capability, ICollection<long?> stops)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = root.OpenSubKey($@"{ConsentStore}\{capability}");
            if (key is not null) CollectRecursively(key, stops);
        }
        catch (UnauthorizedAccessException)
        {
            // A policy-protected hive contributes no readable activity records.
        }
        catch (SecurityException)
        {
            // A policy-protected hive contributes no readable activity records.
        }
        catch (IOException)
        {
            // The registry can change while it is being enumerated; retry next poll.
        }
    }

    private static void CollectRecursively(RegistryKey key, ICollection<long?> stops)
    {
        if (TryReadStop(key.GetValue("LastUsedTimeStop"), out var stop))
            stops.Add(stop);

        foreach (var name in key.GetSubKeyNames())
        {
            try
            {
                using var child = key.OpenSubKey(name);
                if (child is not null) CollectRecursively(child, stops);
            }
            catch (UnauthorizedAccessException)
            {
                // Skip only the inaccessible application entry.
            }
            catch (SecurityException)
            {
                // Skip only the inaccessible application entry.
            }
            catch (IOException)
            {
                // The entry disappeared during enumeration.
            }
        }
    }

    private static bool TryReadStop(object? value, out long stop)
    {
        switch (value)
        {
            case long longValue:
                stop = longValue;
                return true;
            case int intValue:
                stop = intValue;
                return true;
            case string text when long.TryParse(
                text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                stop = parsed;
                return true;
            default:
                stop = default;
                return false;
        }
    }

    private readonly record struct ActivitySnapshot(bool? Microphone, bool? Camera);
}
