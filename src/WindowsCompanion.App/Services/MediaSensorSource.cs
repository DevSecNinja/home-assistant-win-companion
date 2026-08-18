using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.Media.Control;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;

namespace WindowsCompanion_App.Services;

/// <summary>
/// Windows shim for the Now Playing / Media Playing sensors, backed by the
/// System Media Transport Controls (SMTC) session manager.
/// </summary>
/// <remarks>
/// SMTC access lives entirely here; title/attribute formatting stays in
/// <see cref="MediaPlaybackFormatter"/> so it is testable without a real media
/// session. A short poll (rather than SMTC's own change events) keeps this
/// source's shape consistent with the other Windows-state sensors
/// (<see cref="AudioDeviceSensorSource"/>, <see cref="CapabilityUsageSensorSource"/>)
/// and avoids the extra lifetime management that subscribing to
/// <c>CurrentSessionChanged</c>/<c>MediaPropertiesChanged</c> across a
/// changing set of sessions would add.
///
/// Capture is scoped to the enabled/permitted sensor ids: a preview or poll
/// with only <see cref="PlayingId"/> enabled never fetches title, artist or
/// the source app - it only needs a playback status - so enabling the binary
/// sensor alone cannot leak Now Playing metadata, matching the per-sensor
/// isolation <see cref="SensorPreviewGate"/> guarantees elsewhere.
/// </remarks>
public sealed class MediaSensorSource : ISensorSource, IRefreshableSensorSource, ICachedSensorSource
{
    public const string NowPlayingId = "media_now_playing";
    public const string PlayingId = "media_playing";

    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly SensorPreferences _preferences;
    private readonly Func<IReadOnlySet<string>, CancellationToken, Task<MediaSnapshot>> _capture;
    private readonly SensorPollLoop _loop;
    private readonly ChangeGate<MediaSnapshot> _snapshot = new(MediaSnapshot.Empty);

    private Action? _onChanged;
    private volatile IReadOnlySet<string> _lastCapturedIds = new HashSet<string>();

    public MediaSensorSource(
        SensorPreferences preferences,
        Func<IReadOnlySet<string>, CancellationToken, Task<MediaSnapshot>>? capture = null,
        TimeSpan? pollInterval = null)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _capture = capture ?? CaptureCoreAsync;
        _loop = new SensorPollLoop(PollAsync, pollInterval ?? PollInterval);
    }

    public IReadOnlyList<SensorDefinition> Definitions { get; } =
    [
        new(
            NowPlayingId,
            "Now Playing",
            "The title of the media currently playing on this PC, with the "
            + "artist and source app reported as attributes.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false,
            ResourceUsage: "Low. Checks the active media session every 2 seconds. Sends an "
                           + "extra update only when the track or its playback status changes.",
            AutomationIdea: "When a specific app starts playing, activate a media lighting scene.",
            OptInPlaceholder: "Enable to read the currently playing media"),
        new(
            PlayingId,
            "Media Playing",
            "On while Windows reports media actively playing on this PC.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false,
            ResourceUsage: "Low. Shares the same 2-second media check as Now Playing.",
            AutomationIdea: "When media starts playing, dim the lights for a movie scene.",
            OptInPlaceholder: "Enable to read the currently playing media")
    ];

    public IReadOnlyList<Sensor> Read(
        IReadOnlySet<string> enabled, SensorReadContext context) =>
        Build(_snapshot.Current, enabled);

    public IReadOnlyList<Sensor> ReadCached(IReadOnlySet<string> enabled) =>
        Build(_snapshot.Current, enabled);

    public async ValueTask<IReadOnlyList<Sensor>> PreviewAsync(
        IReadOnlySet<string> requested,
        CancellationToken cancellationToken = default)
    {
        var permitted = SensorPreviewGate.Permitted(Definitions, requested, _preferences);
        var readings = new List<Sensor>();

        if (permitted.Count > 0)
        {
            var snapshot = await _capture(permitted, cancellationToken).ConfigureAwait(false);
            readings.AddRange(Build(snapshot, permitted));
        }

        foreach (var definition in Definitions)
        {
            if (requested.Contains(definition.UniqueId) && !permitted.Contains(definition.UniqueId))
            {
                readings.Add(new Sensor
                {
                    UniqueId = definition.UniqueId,
                    Name = definition.Name,
                    State = definition.DisabledPreview
                });
            }
        }

        return readings;
    }

    public void Start(Action onChanged)
    {
        _onChanged = onChanged;
        if (_loop.IsRunning) return;
        _loop.Start();
    }

    public void Stop()
    {
        _loop.Stop();
        _onChanged = null;
    }

    /// <summary>
    /// Runs one collection now and publishes it to <see cref="_snapshot"/>, so
    /// enabling a media sensor gets a settings-sync read of the freshly
    /// captured value instead of racing the next scheduled poll. This never
    /// calls <c>onChanged</c> itself (see <see cref="PollAsync"/>) because the
    /// caller already reads the fresh value directly.
    /// </summary>
    /// <remarks>
    /// <see cref="SensorPollLoop.RunOnceAsync"/> shares its single-flight gate
    /// with the timer: if a scheduled poll is already in flight, this joins
    /// that poll rather than starting a new one. That poll may have captured
    /// with a narrower, now-stale set of enabled ids (e.g. one taken just
    /// before this call's sensor was switched on), so after joining/running
    /// once, a second run is issued whenever the ids it actually captured
    /// with do not cover what this refresh needs - by then no poll can still
    /// be in flight with the stale scope, so the second run is guaranteed to
    /// capture fresh.
    /// </remarks>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var needed = EnabledIds();
        await _loop.RunOnceAsync(cancellationToken).ConfigureAwait(false);

        if (!_lastCapturedIds.IsSupersetOf(needed))
            await _loop.RunOnceAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task PollAsync(SensorPollReason reason, CancellationToken cancellationToken)
    {
        var enabled = EnabledIds();
        _lastCapturedIds = enabled;
        var current = await _capture(enabled, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var changed = _snapshot.TryUpdate(current);
        if (reason == SensorPollReason.Scheduled && changed)
            _onChanged?.Invoke();
    }

    private HashSet<string> EnabledIds() =>
        Definitions.Where(_preferences.IsEnabled)
            .Select(definition => definition.UniqueId)
            .ToHashSet(StringComparer.Ordinal);

    private static IReadOnlyList<Sensor> Build(
        MediaSnapshot snapshot, IReadOnlySet<string> enabled)
    {
        var sensors = new List<Sensor>();

        if (enabled.Contains(NowPlayingId))
        {
            sensors.Add(new Sensor
            {
                UniqueId = NowPlayingId,
                Type = "sensor",
                Name = "Now Playing",
                State = MediaPlaybackFormatter.DescribeTitle(snapshot),
                Icon = "mdi:music",
                Attributes = MediaPlaybackFormatter.BuildAttributes(snapshot)
            });
        }

        if (enabled.Contains(PlayingId))
        {
            var playing = MediaPlaybackFormatter.IsPlaying(snapshot);
            sensors.Add(new Sensor
            {
                UniqueId = PlayingId,
                Type = "binary_sensor",
                Name = "Media Playing",
                State = playing,
                Icon = playing ? "mdi:play-circle" : "mdi:pause-circle-outline"
            });
        }

        return sensors;
    }

    private static async Task<MediaSnapshot> CaptureCoreAsync(
        IReadOnlySet<string> requested, CancellationToken cancellationToken)
    {
        if (requested.Count == 0) return MediaSnapshot.Empty;

        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager
                .RequestAsync()
                .AsTask(cancellationToken)
                .ConfigureAwait(false);
            if (manager is null) return MediaSnapshot.Empty;

            // Windows' "current" session is often just the most recently
            // activated one, not the one actually making sound - a paused
            // player can outrank a playing one. Prefer any session Windows
            // reports as actively playing, falling back to the current
            // session only when nothing is playing. The selection policy
            // itself lives in Core (MediaSessionSelector) so it is unit
            // tested; only the WinRT enumeration and per-session status
            // lookup live here.
            var session = MediaSessionSelector.SelectPlaying(manager.GetSessions(), TryGetStatus)
                ?? manager.GetCurrentSession();
            if (session is null) return MediaSnapshot.Empty;

            var status = TryGetStatus(session);

            // media_playing alone never needs title, artist or the source
            // app: only fetch and resolve that metadata when Now Playing is
            // actually enabled/permitted, so the binary sensor cannot leak it.
            if (!requested.Contains(NowPlayingId))
                return new MediaSnapshot(null, null, null, status);

            var properties = await session.TryGetMediaPropertiesAsync()
                .AsTask(cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var appId = session.SourceAppUserModelId;
            var appName = ResolveAppName(appId) ?? appId;

            return new MediaSnapshot(properties?.Title, properties?.Artist, appName, status);
        }
        catch (COMException)
        {
            return MediaSnapshot.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return MediaSnapshot.Empty;
        }
    }

    /// <summary>
    /// A single session's playback query is read in isolation so that one
    /// misbehaving session (e.g. one that has just closed) cannot fail the
    /// whole scan for an actually-playing session.
    /// </summary>
    private static MediaPlaybackStatus TryGetStatus(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var playback = session.GetPlaybackInfo();
            return playback is null
                ? MediaPlaybackStatus.Closed
                : (MediaPlaybackStatus)(int)playback.PlaybackStatus;
        }
        catch (COMException)
        {
            return MediaPlaybackStatus.Closed;
        }
    }

    /// <summary>
    /// The SMTC session only exposes the source app's AUMID, not a friendly
    /// name. Resolving one is best-effort: any lookup failure (an app that has
    /// since been uninstalled, an invalid id, an access failure, or an OS
    /// older than the API's minimum of Windows 10 2004/10.0.19041 - below this
    /// app's own 10.0.17763 floor) falls back to the raw AUMID in the caller
    /// rather than failing the whole read.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatformGuard("windows10.0.19041")]
    private static bool AppInfoLookupSupported =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);

    private static string? ResolveAppName(string? appUserModelId)
    {
        if (string.IsNullOrWhiteSpace(appUserModelId) || !AppInfoLookupSupported) return null;

        try
        {
            var info = AppInfo.GetFromAppUserModelId(appUserModelId);
            return string.IsNullOrWhiteSpace(info?.DisplayInfo?.DisplayName)
                ? null
                : info.DisplayInfo.DisplayName;
        }
        catch (COMException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }
}
