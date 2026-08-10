using System.Globalization;
using System.Security;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;
using Microsoft.Win32;

namespace WindowsCompanion_App.Services;

public sealed class CapabilityUsageSensorSource : ISensorSource
{
    public const string MicrophoneId = "microphone";
    public const string CameraId = "camera";

    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private const string ConsentStore =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

    private readonly SensorPreferences _preferences;
    private readonly Func<string, CancellationToken, bool> _readCapability;
    private readonly SensorPollLoop _loop;
    private readonly ChangeGate<ActivitySnapshot> _activity = new(default);

    private Action? _onChanged;

    public CapabilityUsageSensorSource(
        SensorPreferences preferences,
        Func<string, CancellationToken, bool>? readCapability = null,
        TimeSpan? pollInterval = null)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _readCapability = readCapability ?? IsCapabilityActive;
        _loop = new SensorPollLoop(PollAsync, pollInterval ?? PollInterval);
    }

    public IReadOnlyList<SensorDefinition> Definitions { get; } =
    [
        new(
            MicrophoneId,
            "Microphone In Use",
            "On while any application is using a microphone.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false,
            ResourceUsage: "Low. Checks Windows every second. Sends an extra update only when "
                           + "microphone use starts or stops.",
            AutomationIdea: "When the microphone is in use, turn the hall light red as an on-air light."),
        new(
            CameraId,
            "Camera In Use",
            "On while any application is using a camera.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false,
            ResourceUsage: "Low. Checks Windows every second. Sends an extra update only when "
                           + "camera use starts or stops.",
            AutomationIdea: "When the camera is in use, turn on a video-call indicator light.")
    ];

    public IReadOnlyList<Sensor> Read(
        IReadOnlySet<string> enabled, SensorReadContext context)
    {
        var snapshot = Capture(enabled, CancellationToken.None);
        return Build(snapshot, enabled);
    }

    public void Start(Action onChanged)
    {
        _onChanged = onChanged;
        if (_loop.IsRunning) return;

        _activity.Seed(Capture(EnabledIds(), CancellationToken.None));
        _loop.Start();
    }

    public void Stop()
    {
        _loop.Stop();
        _onChanged = null;
    }

    private async Task PollAsync(
        SensorPollReason reason,
        CancellationToken cancellationToken)
    {
        var enabled = EnabledIds();
        var current = await Task.Run(
                () => Capture(enabled, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (reason == SensorPollReason.Scheduled && _activity.TryUpdate(current))
            _onChanged?.Invoke();
    }

    private HashSet<string> EnabledIds() =>
        Definitions.Where(_preferences.IsEnabled)
            .Select(definition => definition.UniqueId)
            .ToHashSet(StringComparer.Ordinal);

    private ActivitySnapshot Capture(
        IReadOnlySet<string> enabled,
        CancellationToken cancellationToken) => new(
        enabled.Contains(MicrophoneId)
            ? _readCapability("microphone", cancellationToken)
            : null,
        enabled.Contains(CameraId)
            ? _readCapability("webcam", cancellationToken)
            : null);

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

    private static bool IsCapabilityActive(
        string capability,
        CancellationToken cancellationToken)
    {
        var stops = new List<long?>();
        Collect(RegistryHive.CurrentUser, capability, stops, cancellationToken);
        Collect(RegistryHive.LocalMachine, capability, stops, cancellationToken);
        return CapabilityActivity.IsActive(stops);
    }

    private static void Collect(
        RegistryHive hive,
        string capability,
        ICollection<long?> stops,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = root.OpenSubKey($@"{ConsentStore}\{capability}");
            if (key is not null) CollectRecursively(key, stops, cancellationToken);
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

    private static void CollectRecursively(
        RegistryKey key,
        ICollection<long?> stops,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (TryReadStop(key.GetValue("LastUsedTimeStop"), out var stop))
            stops.Add(stop);

        foreach (var name in key.GetSubKeyNames())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var child = key.OpenSubKey(name);
                if (child is not null)
                    CollectRecursively(child, stops, cancellationToken);
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
