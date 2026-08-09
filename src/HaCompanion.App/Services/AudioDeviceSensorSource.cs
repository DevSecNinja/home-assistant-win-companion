using System.Runtime.InteropServices;
using HaCompanion.Core.Models;
using HaCompanion.Core.Sensors;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;

namespace HaCompanion_App.Services;

public sealed class AudioDeviceSensorSource : ISensorSource
{
    public const string AudioOutputId = "audio_output";
    public const string HeadsetConnectedId = "headset_connected";

    private readonly SensorPreferences _preferences;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly object _gate = new();
    private Action? _onChanged;
    private AudioDeviceSnapshot _snapshot = AudioDeviceSnapshot.Empty;
    private CancellationTokenSource? _pollCancellation;

    public AudioDeviceSensorSource(SensorPreferences preferences)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
    }

    public IReadOnlyList<SensorDefinition> Definitions { get; } =
    [
        new(
            AudioOutputId,
            "Audio Output",
            "The friendly name of the default Windows audio output.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false,
            ResourceUsage: "Enumerates local audio devices every 10 seconds while enabled and "
                           + "requests an immediate batch only when the result changes."),
        new(
            HeadsetConnectedId,
            "Headset Connected",
            "On while Windows exposes a headset, headphones or earbuds audio endpoint.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false,
            ResourceUsage: "Shares one local audio-device scan every 10 seconds with Audio Output "
                           + "and requests an immediate batch only when the result changes.")
    ];

    public IReadOnlyList<Sensor> Read(
        IReadOnlySet<string> enabled, SensorReadContext context)
    {
        AudioDeviceSnapshot snapshot;
        lock (_gate) snapshot = _snapshot;
        return Build(snapshot, enabled);
    }

    public async ValueTask<IReadOnlyList<Sensor>> PreviewAsync(
        IReadOnlySet<string> requested,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await CaptureAsync(requested, cancellationToken).ConfigureAwait(false);
        return Build(snapshot, requested);
    }

    public void Start(Action onChanged)
    {
        _onChanged = onChanged;
        if (_pollCancellation is not null) return;

        _pollCancellation = new CancellationTokenSource();
        _ = PollAsync(_pollCancellation.Token);
    }

    public void Stop()
    {
        var cancellation = Interlocked.Exchange(ref _pollCancellation, null);
        if (cancellation is null) return;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshAsync(notify: true, cancellationToken).ConfigureAwait(false);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                await RefreshAsync(notify: true, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshAsync(bool notify, CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await CaptureAsync(EnabledIds(), cancellationToken).ConfigureAwait(false);
            var changed = false;

            lock (_gate)
            {
                if (current != _snapshot)
                {
                    _snapshot = current;
                    changed = true;
                }
            }

            if (notify && changed) _onChanged?.Invoke();
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<AudioDeviceSnapshot> CaptureAsync(
        IReadOnlySet<string> requested,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            DeviceInformation? defaultDevice = null;
            if (requested.Contains(AudioOutputId))
            {
                var defaultId = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);
                defaultDevice = string.IsNullOrEmpty(defaultId)
                    ? null
                    : await DeviceInformation.CreateFromIdAsync(defaultId);
            }

            var endpoints = new List<string>();
            if (requested.Contains(HeadsetConnectedId))
            {
                var render = await DeviceInformation.FindAllAsync(DeviceClass.AudioRender);
                var capture = await DeviceInformation.FindAllAsync(DeviceClass.AudioCapture);
                endpoints.AddRange(render.Select(device => device.Name));
                endpoints.AddRange(capture.Select(device => device.Name));
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new AudioDeviceSnapshot(
                defaultDevice?.Name,
                HeadsetClassifier.AnyHeadset(endpoints));
        }
        catch (COMException)
        {
            return AudioDeviceSnapshot.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return AudioDeviceSnapshot.Empty;
        }
    }

    private HashSet<string> EnabledIds() =>
        Definitions.Where(_preferences.IsEnabled)
            .Select(definition => definition.UniqueId)
            .ToHashSet(StringComparer.Ordinal);

    private static IReadOnlyList<Sensor> Build(
        AudioDeviceSnapshot snapshot, IReadOnlySet<string> enabled)
    {
        var sensors = new List<Sensor>();

        if (enabled.Contains(AudioOutputId))
        {
            sensors.Add(new Sensor
            {
                UniqueId = AudioOutputId,
                Type = "sensor",
                Name = "Audio Output",
                State = snapshot.DefaultOutputName ?? "Not Connected",
                Icon = "mdi:speaker"
            });
        }

        if (enabled.Contains(HeadsetConnectedId))
        {
            sensors.Add(new Sensor
            {
                UniqueId = HeadsetConnectedId,
                Type = "binary_sensor",
                Name = "Headset Connected",
                State = snapshot.HeadsetConnected,
                Icon = snapshot.HeadsetConnected ? "mdi:headset" : "mdi:headset-off"
            });
        }

        return sensors;
    }

    private sealed record AudioDeviceSnapshot(
        string? DefaultOutputName,
        bool HeadsetConnected)
    {
        public static AudioDeviceSnapshot Empty { get; } = new(null, false);
    }
}
