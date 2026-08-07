using HaCompanion.Core.Abstractions;
using HaCompanion.Core.Models;

namespace HaCompanion.Core.Sensors;

public sealed class WinGetUpdateSensorSource : ISensorSource, IRefreshableSensorSource
{
    public const string WinGetUpdatesId = "winget_updates";

    private readonly IWinGetUpdateProvider _provider;
    private readonly SensorPreferences _preferences;
    private readonly TimeSpan _refreshInterval;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly object _gate = new();
    private readonly object _lifetimeGate = new();
    private WinGetUpdateResult _result = WinGetUpdateResult.Checking;
    private Action? _onChanged;
    private CancellationTokenSource? _pollCancellation;

    public WinGetUpdateSensorSource(
        IWinGetUpdateProvider provider,
        SensorPreferences preferences,
        TimeSpan? refreshInterval = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _refreshInterval = refreshInterval ?? TimeSpan.FromHours(6);
    }

    public IReadOnlyList<SensorDefinition> Definitions { get; } =
    [
        new(
            WinGetUpdatesId,
            "WinGet Updates",
            "Number of application updates available through Windows Package Manager.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false)
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
        CancellationTokenSource cancellation;
        lock (_lifetimeGate)
        {
            if (_pollCancellation is not null) return;
            cancellation = new CancellationTokenSource();
            _pollCancellation = cancellation;
        }
        _ = PollAsync(cancellation);
    }

    public void Stop()
    {
        CancellationTokenSource? cancellation;
        lock (_lifetimeGate)
        {
            cancellation = _pollCancellation;
            _pollCancellation = null;
        }
        if (cancellation is null) return;
        cancellation.Cancel();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        CancellationToken? lifetimeToken;
        lock (_lifetimeGate)
            lifetimeToken = _pollCancellation?.Token;

        if (lifetimeToken is null)
        {
            await RefreshCoreAsync(notify: false, cancellationToken).ConfigureAwait(false);
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, lifetimeToken.Value);
        try
        {
            await RefreshCoreAsync(notify: false, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (lifetimeToken.Value.IsCancellationRequested
                  && !cancellationToken.IsCancellationRequested)
        {
            // The sensor was disabled or its catalog stopped during the refresh.
        }
    }

    private async Task RefreshCoreAsync(bool notify, CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await _provider
                .CheckForUpdatesAsync(cancellationToken)
                .ConfigureAwait(false);

            lock (_gate) _result = current;
            if (notify) _onChanged?.Invoke();
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task PollAsync(CancellationTokenSource cancellation)
    {
        var cancellationToken = cancellation.Token;
        try
        {
            await RefreshCoreAsync(notify: true, cancellationToken).ConfigureAwait(false);
            using var timer = new PeriodicTimer(_refreshInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                await RefreshCoreAsync(notify: true, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_lifetimeGate)
            {
                if (ReferenceEquals(_pollCancellation, cancellation))
                    _pollCancellation = null;
            }
            cancellation.Dispose();
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
