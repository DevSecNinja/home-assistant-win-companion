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
    private readonly HttpClient _http = new();
    private readonly WindowsSecretStore _secrets = new();
    private readonly SessionStore _settings;
    private readonly WindowsSystemStatusProvider _status = new();
    private readonly ToastNotifier _toasts = new();
    private readonly PowerShellWinGetUpdateProvider _winGetUpdates = new();
    private readonly OAuthLoginService _login;
    private readonly ILoggerFactory _loggerFactory =
        LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new FileLoggerProvider(LogLevel.Debug));
            builder.SetMinimumLevel(LogLevel.Debug);
        });

    private ServerConfig? _config;
    private ConnectionManager? _connection;
    private SensorCatalog? _catalog;

    public AppController()
    {
        _login = new OAuthLoginService(_http);
        _settings = new SessionStore(new SettingsStore(), _secrets);
    }

    public ConnectionState State => _connection?.State ?? ConnectionState.Disconnected;

    public string? BaseUrl => _config?.BaseUrl;

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
                ConnectionState.Reconnecting => (false, "Reconnecting…"),
                ConnectionState.Disconnected => (false, "Disconnected"),
                _ when _connection.ConsecutiveFailures > 0 =>
                    (false, $"Reporting failed ({_connection.ConsecutiveFailures}×): {_connection.LastError}"),
                _ when _connection.LastSyncedAt is null => (false, "Waiting for first update"),
                _ when DateTimeOffset.UtcNow - _connection.LastSyncedAt.Value > _connection.SyncInterval * 2.5 =>
                    (false, "No recent update"),
                _ => (true, "Healthy")
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

    public void SaveSensorPreferences()
    {
        if (_config is not null) _settings.Save(_config);
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

    public enum ServerUrlChangeResult
    {
        Changed,
        RequiresSignIn
    }

    /// <summary>Resumes a previously saved session, if one exists and is usable.</summary>
    public async Task<bool> TryResumeAsync(CancellationToken ct = default)
    {
        var config = _settings.Load();
        if (config is null || !config.IsValid()) return false;
        if (string.IsNullOrEmpty(_secrets.Get(AppConstants.RefreshTokenKey))) return false;

        _config = config;
        await BuildAndStartAsync(ct).ConfigureAwait(false);
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

        _secrets.Save(AppConstants.RefreshTokenKey, tokens.RefreshToken);

        var config = _settings.Load() ?? new ServerConfig();
        config.BaseUrl = baseUrl;
        if (string.IsNullOrEmpty(config.DeviceId))
            config.DeviceId = Guid.NewGuid().ToString("N");
        _settings.Save(config);
        _config = config;

        await BuildAndStartAsync(ct, seedAccessToken: tokens.AccessToken, seedExpiresIn: tokens.ExpiresIn)
            .ConfigureAwait(false);
    }

    public async Task<ServerUrlChangeResult> ChangeServerUrlAsync(
        string baseUrl,
        CancellationToken ct = default)
    {
        var config = _config ?? throw new InvalidOperationException("No connected server.");
        var refreshToken = _secrets.Get(AppConstants.RefreshTokenKey);
        if (string.IsNullOrEmpty(refreshToken) || string.IsNullOrEmpty(config.WebhookId))
            return ServerUrlChangeResult.RequiresSignIn;

        var candidate = ServerUrlNormalizer.Normalize(baseUrl);
        candidate = await ResolveBaseUrlAsync(
                candidate,
                ct,
                requireReachableHttp: true)
            .ConfigureAwait(false);
        if (Uri.Compare(
                new Uri(candidate),
                new Uri(config.BaseUrl),
                UriComponents.HttpRequestUrl,
                UriFormat.SafeUnescaped,
                StringComparison.OrdinalIgnoreCase) == 0)
        {
            return ServerUrlChangeResult.Changed;
        }

        TokenResponse token;
        try
        {
            token = await new HaOAuthClient(_http, candidate)
                .RefreshAsync(refreshToken, AppConstants.ClientId, ct)
                .ConfigureAwait(false);
        }
        catch (HomeAssistantAuthException)
        {
            return ServerUrlChangeResult.RequiresSignIn;
        }

        var candidateTokens = new OAuthTokenManager(
            new HaOAuthClient(_http, candidate),
            AppConstants.ClientId,
            () => refreshToken,
            log: _loggerFactory.CreateLogger<OAuthTokenManager>());
        candidateTokens.Seed(token.AccessToken, token.ExpiresIn);
        var client = new HomeAssistantClient(
            _http,
            candidate,
            candidateTokens,
            _loggerFactory.CreateLogger<HomeAssistantClient>());

        if (!await client.ValidateAsync(ct).ConfigureAwait(false))
            throw new InvalidOperationException("The new URL did not return a valid Home Assistant API response.");

        try
        {
            await client.UpdateRegistrationAsync(
                config.WebhookId,
                DeviceInfo.BuildRegistration(config.DeviceId),
                ct).ConfigureAwait(false);
        }
        catch (HomeAssistantAuthException)
        {
            return ServerUrlChangeResult.RequiresSignIn;
        }

        var previous = config.BaseUrl;
        var wasConnected = _connection is not null;
        if (wasConnected)
            await DisconnectAsync().ConfigureAwait(false);

        try
        {
            config.BaseUrl = candidate;
            _settings.Save(config);
            _config = config;
            if (wasConnected)
            {
                await BuildAndStartAsync(
                        ct,
                        seedAccessToken: token.AccessToken,
                        seedExpiresIn: token.ExpiresIn)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            config.BaseUrl = previous;
            _config = config;
            try
            {
                _settings.Save(config);
                if (wasConnected)
                    await BuildAndStartAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Preserve the original failure while making a best effort to restore.
            }
            throw;
        }

        return ServerUrlChangeResult.Changed;
    }

    /// <summary>
    /// Stops reporting to Home Assistant but keeps the saved server and
    /// credentials, so it can be resumed without signing in again.
    /// </summary>
    public async Task DisconnectAsync()
    {
        _catalog?.Stop();
        _catalog = null;

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }

        StateChanged?.Invoke(ConnectionState.Disconnected);
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
        await DisconnectAsync().ConfigureAwait(false);

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

        _secrets.Delete(AppConstants.RefreshTokenKey);
        _settings.Delete();
        _config = null;
        StateChanged?.Invoke(ConnectionState.Disconnected);
    }

    /// <summary>Opens the connected Home Assistant instance in the default browser.</summary>
    public void OpenHomeAssistant()
    {
        var url = _config?.BaseUrl;
        if (string.IsNullOrEmpty(url)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    public SystemStatus GetSystemStatus() => _status.GetStatus();

    private async Task BuildAndStartAsync(
        CancellationToken ct, string? seedAccessToken = null, int seedExpiresIn = 0)
    {
        var config = _config ?? throw new InvalidOperationException("No configuration loaded.");

        var oauth = new HaOAuthClient(_http, config.BaseUrl);
        var tokenManager = new OAuthTokenManager(
            oauth,
            AppConstants.ClientId,
            () => _secrets.Get(AppConstants.RefreshTokenKey),
            log: _loggerFactory.CreateLogger<OAuthTokenManager>());
        if (seedAccessToken is not null)
            tokenManager.Seed(seedAccessToken, seedExpiresIn);

        var client = new HomeAssistantClient(_http, config.BaseUrl, tokenManager,
            _loggerFactory.CreateLogger<HomeAssistantClient>());

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

        var ws = new HaWebSocketClient(
            () => new ClientWebSocketAdapter(), config.BaseUrl, tokenManager, config.WebhookId!,
            _loggerFactory.CreateLogger<HaWebSocketClient>());

        var catalog = new SensorCatalog(
            new ISensorSource[]
            {
                new BatterySensorSource(_status),
                new ActiveSensorSource(config.Sensors),
                new NetworkSensorSource(config.Sensors),
                new WifiSensorSource(config.Sensors),
                new SystemSensorSource(),
                new DisplaySensorSource(),
                new WindowsThemeSensorSource(),
                new LocaleSensorSource(),
                new DiskUsageSensorSource(),
                new LastUpdateSensorSource(),
                new NotificationStateSensorSource(),
                new CapabilityUsageSensorSource(config.Sensors),
                new AudioDeviceSensorSource(config.Sensors),
                new FrontmostAppSensorSource(config.Sensors),
                new WinGetUpdateSensorSource(_winGetUpdates, config.Sensors)
            },
            config.Sensors);
        _catalog = catalog;

        var sensors = new SensorSyncService(client, catalog,
            config.RegisteredSensors,
            () => _settings.Save(config),
            _loggerFactory.CreateLogger<SensorSyncService>());

        var connection = new ConnectionManager(ws, sensors, config.WebhookId!,
            log: _loggerFactory.CreateLogger<ConnectionManager>());
        connection.StateChanged += s => StateChanged?.Invoke(s);
        connection.NotificationReceived += n => _toasts.Show(n);
        _connection = connection;
        connection.Start();

        // Sources push immediately on change; steady state stays at one batch per sync.
        catalog.Start(() => _ = connection.SyncNowAsync());
    }

    /// <summary>
    /// Follows any redirects the server issues for the base URL (typically an
    /// http → https upgrade) and returns the effective base URL. This matters
    /// because redirects rewrite POSTs into GETs, which would make the
    /// <c>/auth/token</c> exchange fail with 405 Method Not Allowed.
    /// </summary>
    private async Task<string> ResolveBaseUrlAsync(
        string baseUrl,
        CancellationToken ct,
        bool requireReachableHttp = false)
    {
        using var timeout = requireReachableHttp
            ? new CancellationTokenSource(TimeSpan.FromSeconds(10))
            : null;
        using var linked = timeout is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        var requestToken = linked?.Token ?? ct;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl);
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken)
                .ConfigureAwait(false);

            var finalUri = response.RequestMessage?.RequestUri;
            if (finalUri is not null)
                return finalUri.GetLeftPart(UriPartial.Authority) + "/";
        }
        catch (OperationCanceledException) when (timeout?.IsCancellationRequested == true)
        {
            throw new InvalidOperationException(
                "The new URL did not respond as an HTTP or HTTPS server within 10 seconds.");
        }
        catch (HttpRequestException ex) when (requireReachableHttp)
        {
            throw new InvalidOperationException(
                "The new URL is not a reachable HTTP or HTTPS Home Assistant endpoint.", ex);
        }
        catch (IOException ex) when (requireReachableHttp)
        {
            throw new InvalidOperationException(
                "The new URL did not speak the HTTP protocol expected by Home Assistant.", ex);
        }
        catch
        {
            if (requireReachableHttp) throw;
            // Unreachable or non-HTTP failure: keep the user's URL and let the
            // OAuth step surface a meaningful error.
        }
        return baseUrl;
    }

    public async ValueTask DisposeAsync()
    {
        _catalog?.Stop();
        if (_connection is not null)
            await _connection.DisposeAsync().ConfigureAwait(false);
        _http.Dispose();
    }
}
