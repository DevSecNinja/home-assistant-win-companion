using HaCompanion.Core.App;
using HaCompanion.Core.HomeAssistant;
using HaCompanion.Core.Models;
using HaCompanion.Core.Sensors;
using HaCompanion_App.Services;
using Microsoft.Extensions.Logging;

namespace HaCompanion_App;

/// <summary>
/// Central coordinator wiring the Windows platform services to the
/// platform-agnostic core: owns the OAuth session, device registration, the
/// live connection, and forwards Home Assistant notifications to Windows toasts.
/// </summary>
public sealed class AppController : IAsyncDisposable
{
    private static readonly TimeSpan NetworkSettleDelay = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http = new();
    private readonly WindowsSecretStore _secrets = new();
    private readonly SessionStore _settings;
    private readonly WindowsSystemStatusProvider _status = new();
    private readonly ToastNotifier _toasts = new();
    private readonly PowerShellWinGetUpdateProvider _winGetUpdates = new();
    private readonly OAuthLoginService _login;
    private readonly WindowsNetworkContextProvider _network = new();
    private readonly ConnectionLifecycle _lifecycle;
    private readonly ILoggerFactory _loggerFactory =
        LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new FileLoggerProvider(LogLevel.Debug));
            builder.SetMinimumLevel(LogLevel.Debug);
        });

    private readonly HttpRouteProbe _probe;

    private ServerConfig? _config;
    private ConnectionManager? _connection;
    private SensorCatalog? _catalog;
    private RouteSupervisor? _supervisor;
    private CancellationTokenSource? _networkSettle;

    public AppController()
    {
        _login = new OAuthLoginService(_http);
        _settings = new SessionStore(new SettingsStore(), _secrets);
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
        : _config.RouteAssignmentPending && _supervisor?.ActiveRoute is null ? RouteStatus.Unassigned
        : _supervisor?.Status ?? RouteStatus.Offline;

    /// <summary>One word for the status view and tray tooltip.</summary>
    public string RouteSummary => RouteState switch
    {
        RouteStatus.Internal => "Internal",
        RouteStatus.External => "External",
        RouteStatus.FailingOver => "Failing over",
        RouteStatus.Unassigned => "Single address",
        _ => "Offline"
    };

    /// <summary>True while a migrated install still has one unclassified address.</summary>
    public bool RouteAssignmentPending => _config?.RouteAssignmentPending ?? false;

    /// <summary>The local network snapshot, for the trusted-network settings UI.</summary>
    public NetworkContext CurrentNetwork => _network.GetCurrent();

    /// <summary>The sensor catalog, once a session exists. Null before sign-in.</summary>
    public SensorCatalog? Catalog => _catalog;

    /// <summary>When sensor states were last pushed to Home Assistant successfully.</summary>
    public DateTimeOffset? LastSyncedAt => _connection?.LastSyncedAt;

    /// <summary>A one-line health verdict for the status view and tray tooltip.</summary>
    public (bool Healthy, string Summary) Health
    {
        get
        {
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
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "ms-settings:privacy-location",
            UseShellExecute = true
        });

    /// <summary>Persists the current sensor choices and pushes them immediately.</summary>
    public async Task ApplySensorChangesAsync()
    {
        if (_config is null) return;
        _settings.Save(_config);
        if (_connection is not null)
            await _connection.SyncNowAsync(SensorReadContext.SettingsChanged).ConfigureAwait(false);
    }

    public Task<bool> IsWinGetModuleInstalledAsync(CancellationToken ct = default) =>
        _winGetUpdates.IsModuleInstalledAsync(ct);

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

    /// <summary>Resumes a previously saved session, if one exists and is usable.</summary>
    /// <remarks>
    /// Taken as an explicit intent, so a background route switch that is already
    /// running is pre-empted and stands down rather than racing this rebuild.
    /// </remarks>
    public async Task<bool> TryResumeAsync(CancellationToken ct = default)
    {
        using var lease = await _lifecycle.AcquireAsync(LifecycleIntent.Start, ct).ConfigureAwait(false);

        var config = _settings.Load();
        if (config is null || !config.IsValid()) return false;
        if (string.IsNullOrEmpty(_secrets.Get(AppConstants.RefreshTokenKey))) return false;

        _config = config;
        // The supervisor captures the config instance, so a reload must discard it
        // rather than leave it deciding routes from an orphaned object.
        _supervisor = null;
        await PrepareRouteAsync(RouteTrigger.Startup, lease.Token).ConfigureAwait(false);
        await BuildAndStartAsync(lease.Token).ConfigureAwait(false);
        return true;
    }

    /// <summary>Runs the interactive OAuth login and starts the connection.</summary>
    public async Task SignInAsync(string baseUrl, CancellationToken ct = default)
    {
        baseUrl = ServerUrlNormalizer.Normalize(baseUrl);
        baseUrl = await ResolveBaseUrlAsync(baseUrl, ct).ConfigureAwait(false);

        var tokens = await _login.SignInAsync(baseUrl, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(tokens.RefreshToken))
            throw new InvalidOperationException("Home Assistant did not return a refresh token.");

        using var lease = await _lifecycle.AcquireAsync(LifecycleIntent.Start, ct).ConfigureAwait(false);

        _secrets.Save(AppConstants.RefreshTokenKey, tokens.RefreshToken);

        var config = _settings.Load() ?? new ServerConfig();
        config.BaseUrl = baseUrl;
        if (string.IsNullOrEmpty(config.DeviceId))
            config.DeviceId = Guid.NewGuid().ToString("N");

        // A single address at sign-in is not evidence of being internal or
        // external, so it is flagged for the user to classify rather than guessed.
        if (config.ConfiguredRoutes().Count == 0) config.RouteAssignmentPending = true;

        _settings.Save(config);
        _config = config;
        _supervisor = null;

        await BuildAndStartAsync(lease.Token,
                seedAccessToken: tokens.AccessToken, seedExpiresIn: tokens.ExpiresIn)
            .ConfigureAwait(false);
    }

    /// <summary>The saved connection settings, as the settings UI edits them.</summary>
    public ConnectionSettingsDraft ConnectionSettings
    {
        get
        {
            var config = _config;
            if (config is null) return new ConnectionSettingsDraft();

            return new ConnectionSettingsDraft
            {
                InternalUrl = config.InternalUrl,
                ExternalUrl = config.ExternalUrl,
                Mode = config.ConnectionMode,
                TrustedNetworks = new TrustedNetworkSettings
                {
                    Ssids = [.. config.TrustedNetworks.Ssids],
                    Bssids = [.. config.TrustedNetworks.Bssids],
                    RequireBssidMatch = config.TrustedNetworks.RequireBssidMatch,
                    TrustWiredNetworks = config.TrustedNetworks.TrustWiredNetworks,
                    ProbeInternalOnUnknownNetworks = config.TrustedNetworks.ProbeInternalOnUnknownNetworks
                }
            };
        }
    }

    /// <summary>
    /// Tests both addresses without changing anything, so the user can see what
    /// would happen before committing.
    /// </summary>
    public Task<RouteValidationReport> TestConnectionSettingsAsync(
        ConnectionSettingsDraft draft, CancellationToken ct = default)
    {
        var config = _config ?? throw new InvalidOperationException("No connected server.");
        return RouteValidator.ValidateAsync(config, draft, _probe, ct);
    }

    /// <summary>
    /// Validates and, only on success, saves the connection settings. Every
    /// failure path leaves the previous working configuration in place.
    /// </summary>
    public async Task<RouteValidationReport> SaveConnectionSettingsAsync(
        ConnectionSettingsDraft draft, CancellationToken ct = default)
    {
        var config = _config ?? throw new InvalidOperationException("No connected server.");

        var report = await RouteValidator.ValidateAsync(config, draft, _probe, ct).ConfigureAwait(false);
        if (!report.CanSave) return report;

        // Reconfigure rather than Start: saving settings while disconnected must
        // not reconnect. It still bumps the generation, so an in-flight route
        // switch stands down instead of racing this rebuild.
        using var lease = await _lifecycle
            .AcquireAsync(LifecycleIntent.Reconfigure, ct)
            .ConfigureAwait(false);

        var previous = Snapshot(config);
        RouteValidator.Apply(config, draft, report);
        _settings.Save(config);

        try
        {
            await PrepareRouteAsync(RouteTrigger.UserRequested, lease.Token).ConfigureAwait(false);
            if (!string.Equals(_supervisor?.ActiveUrl, previous.BaseUrl, StringComparison.OrdinalIgnoreCase)
                && _connection is not null)
            {
                await RestartOnActiveRouteAsync(lease.Token).ConfigureAwait(false);
            }
        }
        catch
        {
            Restore(config, previous);
            _settings.Save(config);

            // The restart may already have torn the connection down. Put the
            // previous configuration back on the air unless the user has since
            // asked for something else, in which case that intent decides.
            _supervisor = null;
            if (lease.IsCurrent)
            {
                try
                {
                    await PrepareRouteAsync(RouteTrigger.UserRequested, lease.Token).ConfigureAwait(false);
                    if (_connection is null)
                        await BuildAndStartAsync(lease.Token).ConfigureAwait(false);
                }
                catch (Exception recovery)
                {
                    _loggerFactory.CreateLogger<AppController>()
                        .LogError(recovery, "Could not restore the previous connection settings.");
                }
            }

            throw;
        }

        RouteChanged?.Invoke();
        return report;
    }

    /// <summary>
    /// Records the user's decision about a migrated single address. Deliberately
    /// explicit: a hostname cannot tell internal from external once split DNS,
    /// reverse proxies or Nabu Casa are involved.
    /// </summary>
    public async Task AssignMigratedRouteAsync(RouteKind route, CancellationToken ct = default)
    {
        using var lease = await _lifecycle
            .AcquireAsync(LifecycleIntent.Reconfigure, ct)
            .ConfigureAwait(false);

        var config = _config ?? throw new InvalidOperationException("No connected server.");
        if (string.IsNullOrWhiteSpace(config.BaseUrl))
            throw new InvalidOperationException("There is no saved address to classify.");

        // Classifying is still a route assignment, so it obeys the same transport
        // rule as the settings panel: a plain-HTTP address cannot become external.
        var url = RouteUrlPolicy.Normalize(config.BaseUrl, route);
        if (!url.Accepted || url.Url is null)
        {
            throw new InvalidOperationException(
                url.Message ?? "The saved address cannot be used for that route.");
        }

        config.SetRoute(route, url.Url);
        config.RouteAssignmentPending = false;
        config.LastSuccessfulRoute = route;
        _settings.Save(config);
        _supervisor ??= CreateSupervisor(config);
        _supervisor.Adopt(route, url.Url);
        RouteChanged?.Invoke();
    }

    /// <summary>
    /// Which classification the saved address most likely is. A suggestion for the
    /// UI to preselect; the user still chooses.
    /// </summary>
    public RouteKind SuggestRouteForSavedUrl() =>
        _config is not null && RouteUrlPolicy.LooksPrivate(_config.BaseUrl)
            ? RouteKind.Internal
            : RouteKind.External;

    /// <summary>
    /// Addresses Home Assistant itself reports, offered as suggestions only. The
    /// cloudhook URL is never offered: it embeds the webhook capability secret.
    /// </summary>
    public async Task<(string? Internal, string? External)> SuggestedUrlsAsync(CancellationToken ct = default)
    {
        var config = _config;
        if (config is null) return (null, null);

        try
        {
            var client = CreateClient(config, ActiveUrl(config), out _);
            var instance = await client.GetConfigAsync(ct).ConfigureAwait(false);
            var external = instance?.ExternalUrl ?? config.RemoteUiUrl;
            return (instance?.InternalUrl, external);
        }
        catch
        {
            return (null, config.RemoteUiUrl);
        }
    }

    /// <summary>Re-checks routing now, on the user's command.</summary>
    public async Task RefreshRouteAsync(CancellationToken ct = default)
    {
        await EvaluateRouteAsync(RouteTrigger.UserRequested, ct).ConfigureAwait(false);
        RouteChanged?.Invoke();
    }

    private static (string BaseUrl, string? Internal, string? External, ConnectionMode Mode,
        TrustedNetworkSettings Trusted, bool Pending) Snapshot(ServerConfig config) =>
        (config.BaseUrl, config.InternalUrl, config.ExternalUrl, config.ConnectionMode,
            config.TrustedNetworks, config.RouteAssignmentPending);

    private static void Restore(
        ServerConfig config,
        (string BaseUrl, string? Internal, string? External, ConnectionMode Mode,
            TrustedNetworkSettings Trusted, bool Pending) snapshot)
    {
        config.BaseUrl = snapshot.BaseUrl;
        config.InternalUrl = snapshot.Internal;
        config.ExternalUrl = snapshot.External;
        config.ConnectionMode = snapshot.Mode;
        config.TrustedNetworks = snapshot.Trusted;
        config.RouteAssignmentPending = snapshot.Pending;
    }

    /// <summary>
    /// Stops reporting to Home Assistant but keeps the saved server and
    /// credentials, so it can be resumed without signing in again.
    /// </summary>
    /// <remarks>
    /// Marks the connection as unwanted, so a route switch that was already queued
    /// cannot bring it back after the user has ended it.
    /// </remarks>
    public async Task DisconnectAsync()
    {
        using var lease = await _lifecycle
            .AcquireAsync(LifecycleIntent.Stop)
            .ConfigureAwait(false);

        await DisconnectCoreAsync().ConfigureAwait(false);
        StateChanged?.Invoke(ConnectionState.Disconnected);
    }

    /// <summary>
    /// Tears the connection down. Callers must already hold the lifecycle lease;
    /// taking it here would deadlock the transitions that disconnect as one step
    /// of a larger change.
    /// </summary>
    private async Task DisconnectCoreAsync()
    {
        _catalog?.Stop();
        _catalog = null;

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }

    /// <summary>Resumes reporting after a <see cref="DisconnectAsync"/>.</summary>
    public Task ReconnectAsync(CancellationToken ct = default) => TryResumeAsync(ct);

    /// <summary>Pushes all enabled sensors right now, on the user's command.</summary>
    public async Task ForcePushAsync()
    {
        var connection = _connection;
        var catalog = _catalog;
        if (connection is null || catalog is null) return;

        await catalog.RefreshAsync().ConfigureAwait(false);
        if (!ReferenceEquals(connection, _connection)
            || !ReferenceEquals(catalog, _catalog))
        {
            return;
        }

        await connection.SyncNowAsync(new SensorReadContext("Manual")).ConfigureAwait(false);
    }

    /// <summary>
    /// Forgets the server entirely: revokes the refresh token with Home Assistant
    /// and removes all stored credentials and configuration. The user must sign in
    /// again afterwards.
    /// </summary>
    public async Task RemoveServerAsync(CancellationToken ct = default)
    {
        using var lease = await _lifecycle
            .AcquireAsync(LifecycleIntent.Forget, ct)
            .ConfigureAwait(false);

        await DisconnectCoreAsync().ConfigureAwait(false);

        var refresh = _secrets.Get(AppConstants.RefreshTokenKey);
        if (!string.IsNullOrEmpty(refresh) && _config is not null && _config.IsValid())
        {
            try
            {
                await new HaOAuthClient(_http, _config.BaseUrl).RevokeAsync(refresh, ct).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort revoke; still clear local state below.
            }
        }

        // Cleared last, and under the lease, so no route switch can write the
        // settings file back out after it has been deleted.
        _secrets.Delete(AppConstants.RefreshTokenKey);
        _settings.Delete();
        _config = null;
        _supervisor = null;
        StateChanged?.Invoke(ConnectionState.Disconnected);
    }

    /// <summary>Opens the connected Home Assistant instance in the default browser.</summary>
    public void OpenHomeAssistant()
    {
        var url = BaseUrl;
        if (string.IsNullOrEmpty(url)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    public SystemStatus GetSystemStatus() => _status.GetStatus();

    /// <summary>
    /// Builds the REST, WebSocket and sensor stack and puts it on the air.
    /// Callers must hold the lifecycle lease.
    /// </summary>
    private async Task BuildAndStartAsync(
        CancellationToken ct, string? seedAccessToken = null, int seedExpiresIn = 0)
    {
        // Defensive invariant: there is exactly one live manager at a time. Even if
        // a caller forgets to tear down first, the old WebSocket and sync loops are
        // stopped here rather than left running invisibly alongside the new ones.
        if (_connection is not null || _catalog is not null)
        {
            _loggerFactory.CreateLogger<AppController>()
                .LogWarning("A connection was still live at build time; disposing it first.");
            await DisconnectCoreAsync().ConfigureAwait(false);
        }

        var config = _config ?? throw new InvalidOperationException("No configuration loaded.");
        var url = ActiveUrl(config);

        var client = CreateClient(config, url, out var tokenManager);
        if (seedAccessToken is not null)
            tokenManager.Seed(seedAccessToken, seedExpiresIn);

        var registration = DeviceInfo.BuildRegistration(config.DeviceId);
        if (!config.Registered)
        {
            var response = await client.RegisterDeviceAsync(registration, ct).ConfigureAwait(false);
            config.WebhookId = response.WebhookId;
            config.CloudhookUrl = response.CloudhookUrl;
            config.RemoteUiUrl = response.RemoteUiUrl;
            _settings.Save(config);
        }
        else
        {
            // Existing registrations may predate local-push support, which is what
            // makes this PC show up as a notify target; re-declare it on every start.
            try
            {
                await client.UpdateRegistrationAsync(config.WebhookId!, registration, ct).ConfigureAwait(false);
            }
            catch
            {
                // Non-fatal: sensors still work, notifications may not.
            }
        }

        await LearnInstanceIdentityAsync(client, config, ct).ConfigureAwait(false);

        var ws = new HaWebSocketClient(
            () => new ClientWebSocketAdapter(), url, tokenManager, config.WebhookId!,
            _loggerFactory.CreateLogger<HaWebSocketClient>());

        var catalog = new SensorCatalog(
            new ISensorSource[]
            {
                new BatterySensorSource(_status),
                new ActiveSensorSource(config.Sensors),
                new NetworkSensorSource(),
                new WifiSensorSource(config.Sensors),
                new SystemSensorSource(),
                new LastUpdateSensorSource(),
                new NotificationStateSensorSource(),
                new CapabilityUsageSensorSource(config.Sensors),
                new AudioDeviceSensorSource(config.Sensors),
                new WinGetUpdateSensorSource(_winGetUpdates, config.Sensors)
            },
            config.Sensors);
        _catalog = catalog;

        var sensors = new SensorSyncService(client, catalog,
            config.RegisteredSensors,
            () => _settings.Save(config),
            _loggerFactory.CreateLogger<SensorSyncService>());

        var connection = new ConnectionManager(ws, sensors, config.WebhookId!,
            log: _loggerFactory.CreateLogger<ConnectionManager>(),
            route: _supervisor?.ActiveRoute);
        connection.StateChanged += s => StateChanged?.Invoke(s);
        connection.NotificationReceived += n => _toasts.Show(n);
        connection.RouteUnhealthy += _ => RequestRouteEvaluation(RouteTrigger.ConnectionFailed);
        _connection = connection;
        connection.Start();

        // Sources push immediately on change; steady state stays at one batch per sync.
        catalog.Start(() => _ = connection.SyncNowAsync());
    }

    private string ActiveUrl(ServerConfig config) => _supervisor?.ActiveUrl ?? config.BaseUrl;

    private HomeAssistantClient CreateClient(ServerConfig config, string url, out OAuthTokenManager tokens)
    {
        tokens = new OAuthTokenManager(
            new HaOAuthClient(_http, url),
            AppConstants.ClientId,
            () => _secrets.Get(AppConstants.RefreshTokenKey),
            log: _loggerFactory.CreateLogger<OAuthTokenManager>());

        return new HomeAssistantClient(_http, url, tokens,
            _loggerFactory.CreateLogger<HomeAssistantClient>());
    }

    private RouteSupervisor CreateSupervisor(ServerConfig config)
    {
        var supervisor = new RouteSupervisor(
            config, _probe, log: _loggerFactory.CreateLogger<RouteSupervisor>());
        supervisor.RouteActivated += _ => RouteChanged?.Invoke();
        return supervisor;
    }

    /// <summary>
    /// Records Home Assistant's own device id for this registration the first time
    /// it is seen. It is what later proves a second address is the same instance.
    /// </summary>
    private async Task LearnInstanceIdentityAsync(
        HomeAssistantClient client, ServerConfig config, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(config.InstanceDeviceId) || string.IsNullOrEmpty(config.WebhookId))
            return;

        try
        {
            var info = await client.GetInstanceInfoAsync(config.WebhookId, ct).ConfigureAwait(false);
            if (info?.DeviceId is null) return;
            config.InstanceDeviceId = info.DeviceId;
            _settings.Save(config);
        }
        catch
        {
            // Identity is a nicety at startup; the validation flow re-reads it.
        }
    }

    /// <summary>
    /// Picks the address to start on. When nothing can be validated - offline
    /// startup being the normal case - the last address that worked is used and
    /// the connection's own backoff takes over, so the app still comes up.
    /// </summary>
    private async Task PrepareRouteAsync(RouteTrigger trigger, CancellationToken ct)
    {
        var config = _config!;
        _supervisor ??= CreateSupervisor(config);

        if (config.ConfiguredRoutes().Count == 0) return;

        var decision = await _supervisor
            .EvaluateAsync(_network.GetCurrent(), trigger, ct)
            .ConfigureAwait(false);

        if (decision.Kind is RouteDecisionKind.Activated or RouteDecisionKind.Unchanged)
        {
            _settings.Save(config);
            return;
        }

        if (_supervisor.ActiveRoute is not null) return;

        var fallback = config.LastSuccessfulRoute is { } last && config.HasRoute(last)
            ? last
            : config.ConfiguredRoutes()[0];
        if (config.UrlFor(fallback) is { } url)
        {
            config.BaseUrl = url;
            _supervisor.Adopt(fallback, url);
        }
    }

    private void OnNetworkChanged()
    {
        if (_config is null || _connection is null) return;

        // Let the network settle first: a transition produces a burst of events,
        // and a captive portal answers before it lets anything through.
        var settle = new CancellationTokenSource();
        Interlocked.Exchange(ref _networkSettle, settle)?.Cancel();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(NetworkSettleDelay, settle.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await EvaluateRouteAsync(RouteTrigger.NetworkChanged, CancellationToken.None)
                .ConfigureAwait(false);
        });
    }

    private void RequestRouteEvaluation(RouteTrigger trigger) =>
        _ = Task.Run(() => EvaluateRouteAsync(trigger, CancellationToken.None));

    /// <summary>
    /// Re-decides the route and, only when a different address actually proved
    /// usable, rebuilds the clients on it. The refresh token, webhook id and every
    /// registered sensor are untouched, so nothing re-registers.
    /// </summary>
    private async Task EvaluateRouteAsync(RouteTrigger trigger, CancellationToken ct)
    {
        // Never queues: if a user action is running, this switch is dropped rather
        // than applied afterwards to a connection the user may have just ended.
        using var lease = await _lifecycle.TryAcquireRouteSwitchAsync(ct).ConfigureAwait(false);
        if (lease is null) return;

        var config = _config;
        var supervisor = _supervisor;
        if (config is null || supervisor is null) return;
        if (config.ConfiguredRoutes().Count == 0) return;

        try
        {
            var before = supervisor.ActiveUrl;
            var decision = await supervisor
                .EvaluateAsync(_network.GetCurrent(), trigger, lease.Token)
                .ConfigureAwait(false);

            if (decision.Kind != RouteDecisionKind.Activated) return;

            _settings.Save(config);
            if (string.Equals(before, decision.Url, StringComparison.OrdinalIgnoreCase)) return;
            if (_connection is null) return;

            await RestartOnActiveRouteAsync(lease.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Pre-empted by a user action, which now owns the connection's fate.
            _loggerFactory.CreateLogger<AppController>()
                .LogDebug("Route switch abandoned; the connection was changed by the user.");
        }
        catch (Exception ex)
        {
            _loggerFactory.CreateLogger<AppController>()
                .LogWarning(ex, "Route evaluation failed ({Trigger}).", trigger);
        }
    }

    /// <summary>
    /// Tears the REST and WebSocket clients down and rebuilds them on the active
    /// address, which re-opens the push notification channel and resumes sensor
    /// sync without re-registering the device.
    /// </summary>
    /// <remarks>
    /// Calls the core teardown rather than <see cref="DisconnectAsync"/>: every
    /// caller already holds the lifecycle lease, so re-acquiring it would deadlock.
    /// </remarks>
    private async Task RestartOnActiveRouteAsync(CancellationToken ct)
    {
        await DisconnectCoreAsync().ConfigureAwait(false);
        await BuildAndStartAsync(ct).ConfigureAwait(false);
        RouteChanged?.Invoke();
    }

    /// <summary>
    /// Follows any redirects the server issues for the base URL (typically an
    /// http → https upgrade) and returns the effective base URL. This matters
    /// because redirects rewrite POSTs into GETs, which would make the
    /// <c>/auth/token</c> exchange fail with 405 Method Not Allowed.
    /// </summary>
    private async Task<string> ResolveBaseUrlAsync(string baseUrl, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl);
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            var finalUri = response.RequestMessage?.RequestUri;
            if (finalUri is not null)
                return finalUri.GetLeftPart(UriPartial.Authority) + "/";
        }
        catch
        {
            // Unreachable or non-HTTP failure: keep the user's URL and let the
            // OAuth step surface a meaningful error.
        }
        return baseUrl;
    }

    public async ValueTask DisposeAsync()
    {
        _network.NetworkChanged -= OnNetworkChanged;
        _network.Stop();
        _networkSettle?.Cancel();
        _catalog?.Stop();
        if (_connection is not null)
            await _connection.DisposeAsync().ConfigureAwait(false);
        _lifecycle.Dispose();
        _http.Dispose();
    }
}
