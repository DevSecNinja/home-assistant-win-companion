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

    /// <summary>Persists the current sensor choices and pushes them immediately.</summary>
    public async Task ApplySensorChangesAsync()
    {
        if (_config is null) return;
        _settings.Save(_config);
        if (_connection is not null)
            await _connection.SyncNowAsync(SensorReadContext.SettingsChanged).ConfigureAwait(false);
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
        baseUrl = NormalizeBaseUrl(baseUrl);
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
        if (_connection is null) return;
        await _connection.SyncNowAsync(new SensorReadContext("Manual")).ConfigureAwait(false);
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
                new NetworkSensorSource(),
                new SystemSensorSource(),
                new LastUpdateSensorSource()
            },
            config.Sensors);
        _catalog = catalog;

        var sensors = new SensorSyncService(client, catalog,
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

    private static string NormalizeBaseUrl(string baseUrl)
    {
        baseUrl = baseUrl.Trim();
        if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            // Default to HTTPS: an http:// guess against a TLS-only instance would be
            // redirected, and redirects downgrade POSTs to GETs (breaking /auth/token).
            baseUrl = "https://" + baseUrl;
        }
        return baseUrl.TrimEnd('/') + "/";
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
        _catalog?.Stop();
        if (_connection is not null)
            await _connection.DisposeAsync().ConfigureAwait(false);
        _http.Dispose();
    }
}


