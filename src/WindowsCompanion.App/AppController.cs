using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using WindowsCompanion.Core.Abstractions;
using WindowsCompanion.Core.App;
using WindowsCompanion.Core.HomeAssistant;
using WindowsCompanion.Core.Lifecycle;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;
using WindowsCompanion.Core.Updates;
using WindowsCompanion_App.Services;

namespace WindowsCompanion_App;

/// <summary>
/// Central coordinator wiring the Windows platform services to the
/// platform-agnostic core: owns the OAuth session, device registration, the
/// live connection, and forwards Home Assistant notifications to Windows toasts.
/// </summary>
public sealed partial class AppController : IAsyncDisposable
{
    private static readonly TimeSpan NetworkSettleDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long the companion may spend telling Home Assistant that the machine is
    /// going away. Short by design: Windows terminates applications a few seconds
    /// into a shutdown, and waiting longer risks holding up the very thing we are
    /// reporting.
    /// </summary>
    private static readonly TimeSpan FinalLifecyclePushTimeout = TimeSpan.FromSeconds(2);

    private readonly HttpClient _http;
    private readonly ISecretStore _secrets;
    private readonly HttpClient _updateHttp;
    private readonly HttpClient _updateAssetHttp;
    private readonly IUpdateNotificationSink _updateNotifications;
    private readonly bool _enableStartupUpdates;
    private readonly SessionStore _settings;
    private readonly ISystemStatusProvider _status;
    private readonly INotificationSink _notifications;
    private readonly IWinGetUpdateProvider _winGetUpdates;
    private readonly IUriLauncher _uriLauncher;
    private readonly OAuthLoginService _login;
    private readonly INetworkContextProvider _network;
    private readonly ConnectionLifecycle _lifecycle;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Func<IHaSocket> _webSocketFactory;
    private readonly Func<ServerConfig, LifecycleCoordinator, ILifecycleSignalSource,
        IReadOnlyList<ISensorSource>> _sensorSourceFactory;
    private readonly Func<ILifecycleJournal> _lifecycleJournalFactory;
    private readonly Func<ILifecycleSignalSource> _lifecycleSignalSourceFactory;
    private readonly IReadOnlyList<object> _ownedDependencies;

    private readonly HttpRouteProbe _probe;
    private readonly StartupUpdateService _startupUpdates;
    private readonly CancellationTokenSource _updateCheckCancellation = new();
    private readonly UpdateInstaller _updateInstaller;
    private readonly UpdateArchitecture _updateArchitecture;
    private readonly LastInstallResult? _lastInstallResult;

    private ServerConfig? _config;
    private ConnectionManager? _connection;
    private SensorCatalog? _catalog;
    private DemoSession? _demo;
    private RouteSupervisor? _supervisor;
    private string? _instanceVersion;
    private string? _instanceOsVersion;
    private CancellationTokenSource? _networkSettle;
    private NetworkContext? _lastNetwork;
    private int _disposeStarted;
    private Task? _updateCheckTask;
    private int _updateCheckStarted;

    public AppController() : this(AppControllerDependencies.CreateProduction())
    {
    }

    internal AppController(AppControllerDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        _http = dependencies.HttpClient.Value;
        _secrets = dependencies.SecretStore.Value;
        _status = dependencies.SystemStatus.Value;
        _notifications = dependencies.NotificationSink.Value;
        _winGetUpdates = dependencies.WinGetUpdates.Value;
        _uriLauncher = dependencies.UriLauncher.Value;
        _network = dependencies.Network.Value;
        _loggerFactory = dependencies.LoggerFactory.Value;
        _webSocketFactory = dependencies.WebSocketFactory;
        _sensorSourceFactory = dependencies.SensorSourceFactory;
        _lifecycleJournalFactory = dependencies.LifecycleJournalFactory;
        _lifecycleSignalSourceFactory = dependencies.LifecycleSignalSourceFactory;
        _ownedDependencies = dependencies.OwnedValues().ToArray();
        _updateHttp = dependencies.UpdateHttpClient?.Value ?? _http;
        _updateAssetHttp = dependencies.UpdateAssetHttpClient?.Value ?? _updateHttp;
        _updateNotifications = dependencies.UpdateNotificationSink?.Value
            ?? new NoOpUpdateNotificationSink();
        _enableStartupUpdates = dependencies.EnableStartupUpdates;
        var installedBuild = InstalledBuildResolver.Current();
        _startupUpdates = new StartupUpdateService(
            installedBuild,
            new GitHubReleaseClient(
                _updateHttp,
                installedBuild.Version?.ToString() ?? "source"),
            new UpdateNotificationSink(NotifyUpdateAvailable),
            _loggerFactory.CreateLogger<StartupUpdateService>());
        _startupUpdates.StateChanged += OnUpdateStateChanged;
        _updateArchitecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? UpdateArchitecture.Arm64
            : UpdateArchitecture.X64;
        _updateInstaller = new UpdateInstaller(
            new UpdatePackageDownloader(_updateAssetHttp),
            new UpdatePackageVerifier(
                _updateHttp,
                installedBuild.Version?.ToString() ?? "0.0.0",
                _loggerFactory.CreateLogger<UpdatePackageVerifier>(),
                _updateAssetHttp),
            new SilentUpdateInstaller(),
            _loggerFactory.CreateLogger<UpdateInstaller>());
        _updateInstaller.StateChanged += OnUpdateInstallStateChanged;
        _lastInstallResult = SilentUpdateInstaller.TakeLastInstallResult();
        _login = new OAuthLoginService(_http, _uriLauncher);
        _settings = new SessionStore(dependencies.SettingsStore.Value, _secrets);
        _lifecycle = new ConnectionLifecycle(_loggerFactory.CreateLogger<ConnectionLifecycle>());
        _probe = new HttpRouteProbe(
            _http,
            () => _secrets.Get(AppConstants.RefreshTokenKey),
            AppConstants.ClientId,
            log: _loggerFactory.CreateLogger<HttpRouteProbe>());
        _network.NetworkChanged += OnNetworkChanged;
        _network.Start();
    }

    public ConnectionState State => _connection?.State ?? ConnectionState.Disconnected;

    public string? BaseUrl => _supervisor?.ActiveUrl ?? _config?.BaseUrl;

    /// <summary>Which address is in use, and how routing is currently doing.</summary>
    public RouteStatus RouteState =>
        _config is null ? RouteStatus.Offline
        : !_config.UseSeparateUrls ? RouteStatus.SingleUrl
        : _supervisor?.Status ?? RouteStatus.Offline;

    /// <summary>One word for the status view and tray tooltip.</summary>
    public string RouteSummary => IsDemoMode ? DemoSession.RouteSummary : RouteState switch
    {
        RouteStatus.Internal => "Internal",
        RouteStatus.External => "External",
        RouteStatus.FailingOver => "Failing over",
        RouteStatus.SingleUrl => "Single URL",
        _ => "Offline"
    };

    /// <summary>
    /// HA version string for the settings card, e.g. "HA 2025.1.0" or
    /// "HA 2025.1.0 · OS 14.2". Null when disconnected or version unknown.
    /// </summary>
    public string? VersionSummary
    {
        get
        {
            if (State != ConnectionState.Connected) return null;
            var version = _supervisor?.ActiveInstanceVersion ?? _instanceVersion;
            if (string.IsNullOrEmpty(version)) return null;
            return string.IsNullOrEmpty(_instanceOsVersion)
                ? $"HA {version}"
                : $"HA {version} · OS {_instanceOsVersion}";
        }
    }

    /// <summary>The local network snapshot, for the trusted-network settings UI.</summary>
    public NetworkContext CurrentNetwork => _network.GetCurrent();

    /// <summary>The sensor catalog, once a session exists. Null before sign-in.</summary>
    public SensorCatalog? Catalog => _catalog;

    /// <summary>Reads local previews, refreshing enabled demo-only sources once.</summary>
    public Task<IReadOnlyDictionary<string, string>> PreviewSensorsAsync(
        CancellationToken cancellationToken = default) =>
        _demo is not null
            ? _demo.PreviewAsync(cancellationToken)
            : _catalog?.PreviewAsync(cancellationToken)
              ?? Task.FromResult<IReadOnlyDictionary<string, string>>(
                  new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>Reads cached enabled previews without bypassing source-owned collection cadence.</summary>
    public Task<IReadOnlyDictionary<string, string>> PreviewEnabledSensorsAsync(
        CancellationToken cancellationToken = default) =>
        _demo is not null
            ? _demo.PreviewAsync(cancellationToken)
            : Task.FromResult(
                _catalog?.PreviewEnabled()
                ?? (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(
                    StringComparer.Ordinal));

    /// <summary>When sensor states were last pushed to Home Assistant successfully.</summary>
    public DateTimeOffset? LastSyncedAt => _connection?.LastSyncedAt;

    /// <summary>A one-line health verdict for the status view and tray tooltip.</summary>
    public (bool Healthy, string Summary) Health
    {
        get
        {
            if (IsDemoMode) return (false, DemoSession.HealthSummary);
            if (_connection is null) return (false, "Not connected");

            return _connection.State switch
            {
                ConnectionState.AuthError => (false, "Sign-in required"),
                ConnectionState.Connecting => (false, "Connecting…"),
                ConnectionState.Reconnecting when RouteState == RouteStatus.FailingOver =>
                    (false, "Trying the other address…"),
                ConnectionState.Reconnecting => (false, "Reconnecting…"),
                ConnectionState.Disconnected => (false, "Disconnected"),
                _ when _connection.ConsecutiveFailures > 0 =>
                    (false, $"Reporting failed ({_connection.ConsecutiveFailures}×): {_connection.LastError}"),
                _ when _connection.LastSyncedAt is null => (false, "Waiting for first update"),
                _ when DateTimeOffset.UtcNow - _connection.LastSyncedAt.Value > _connection.SyncInterval * 2.5 =>
                    (false, "No recent update"),
                _ => (true, $"Healthy ({RouteSummary})")
            };
        }
    }

    /// <summary>Opens the current log file in the user's default text editor.</summary>
    public void OpenLogFile()
    {
        var path = FileLoggerProvider.CurrentLogFile;
        if (!File.Exists(path)) File.WriteAllText(path, string.Empty);

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    public void OpenLocationSettings() =>
        _ = _uriLauncher.LaunchAsync(new Uri("ms-settings:privacy-location"));

    /// <summary>Persists the current sensor choices and pushes them immediately.</summary>
    public async Task ApplySensorChangesAsync()
    {
        if (IsDemoMode || _config is null) return;
        _settings.Save(_config);
        if (_connection is not null)
            await _connection.SyncNowAsync(SensorReadContext.SettingsChanged).ConfigureAwait(false);
    }

    public void SaveSensorPreferences()
    {
        if (IsDemoMode) return;
        if (_config is not null) _settings.Save(_config);
    }

    public bool HasSavedSession
    {
        get
        {
            var cfg = _settings.Load();
            return cfg is not null && cfg.IsValid()
                   && !string.IsNullOrEmpty(_secrets.Get(AppConstants.RefreshTokenKey));
        }
    }

    public event Action<ConnectionState>? StateChanged;

    /// <summary>Raised when routing changes, so the UI can refresh its labels.</summary>
    public event Action? RouteChanged;
}
