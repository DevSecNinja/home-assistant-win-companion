using WindowsCompanion.Core.Abstractions;
using WindowsCompanion.Core.Models;

namespace WindowsCompanion.Core.Sensors;

public sealed class WinGetUpdateSensorSource : ISensorSource, IRefreshableSensorSource
{
    public const string WinGetUpdatesId = "winget_updates";

    private readonly IWinGetUpdateProvider _provider;
    private readonly SensorPreferences _preferences;
    private readonly SensorPollLoop _loop;
    private readonly object _gate = new();

    /// <summary>What Home Assistant has already been told; only a move here is news.</summary>
    private readonly ChangeGate<(WinGetUpdateStatus Status, int Count)> _published =
        new((WinGetUpdateStatus.Checking, 0));

    private WinGetUpdateResult _result = WinGetUpdateResult.Checking;
    private Action? _onChanged;

    public WinGetUpdateSensorSource(
        IWinGetUpdateProvider provider,
        SensorPreferences preferences,
        TimeSpan? refreshInterval = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _loop = new SensorPollLoop(CheckAsync, refreshInterval ?? TimeSpan.FromHours(6));
    }

    public IReadOnlyList<SensorDefinition> Definitions { get; } =
    [
        new(
            WinGetUpdatesId,
            "WinGet Updates",
            "Number of application updates available through Windows Package Manager.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false,
            ResourceUsage: "Moderate. Checks for app updates when enabled and every 6 hours. This "
                           + "may use your internet connection. Sends an extra update only when the "
                           + "number of available updates changes.",
            AutomationIdea: "When updates are available, send a weekly reminder to install them.")
    ];

    public IReadOnlyList<Sensor> Read(
        IReadOnlySet<string> enabled, SensorReadContext context)
    {
        if (!enabled.Contains(WinGetUpdatesId)) return [];

        WinGetUpdateResult result;
        lock (_gate) result = _result;

        return
        [
            new()
            {
                UniqueId = WinGetUpdatesId,
                Type = "sensor",
                Name = "WinGet Updates",
                State = result.Status == WinGetUpdateStatus.Ready
                    ? result.Packages.Count
                    : "unavailable",
                EntityCategory = "diagnostic",
                Icon = result.Status == WinGetUpdateStatus.Ready && result.Packages.Count > 0
                    ? "mdi:package-up"
                    : "mdi:package-variant"
            }
        ];
    }

    public ValueTask<IReadOnlyList<Sensor>> PreviewAsync(
        IReadOnlySet<string> requested,
        CancellationToken cancellationToken = default)
    {
        var definition = Definitions[0];
        var text = !_preferences.IsEnabled(definition)
            ? "Enable to check for updates"
            : DescribeCurrentResult();

        return ValueTask.FromResult<IReadOnlyList<Sensor>>(
        [
            new()
            {
                UniqueId = WinGetUpdatesId,
                Name = "WinGet Updates",
                State = text
            }
        ]);
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
            .CheckForUpdatesAsync(cancellationToken)
            .ConfigureAwait(false);

        lock (_gate) _result = current;

        // A manual refresh is followed by a push anyway, and a scheduled check
        // that finds the same update count is not worth waking the sync for.
        if (reason == SensorPollReason.Scheduled
            && _published.TryUpdate((current.Status, current.Packages.Count)))
        {
            _onChanged?.Invoke();
        }
    }

    private string DescribeCurrentResult()
    {
        WinGetUpdateResult result;
        lock (_gate) result = _result;

        return result.Status switch
        {
            WinGetUpdateStatus.Checking => "Checking for updates...",
            WinGetUpdateStatus.Ready when result.Packages.Count == 0 => "No updates available",
            WinGetUpdateStatus.Ready => string.Join(
                Environment.NewLine,
                result.Packages.Select(package =>
                    $"{package.Name}: {package.InstalledVersion} -> {package.AvailableVersion}")),
            _ => result.Error ?? "WinGet update status is unavailable"
        };
    }
}
