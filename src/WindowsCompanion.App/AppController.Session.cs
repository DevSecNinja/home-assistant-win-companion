using Microsoft.Extensions.Logging;
using WindowsCompanion.Core.App;
using WindowsCompanion.Core.HomeAssistant;
using WindowsCompanion.Core.Lifecycle;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;
using WindowsCompanion_App.Services;

namespace WindowsCompanion_App;

public sealed partial class AppController
{
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
        config.SetSingleUrl(baseUrl);

        _settings.Save(config);
        _config = config;
        _supervisor = null;

        await BuildAndStartAsync(lease.Token,
                seedAccessToken: tokens.AccessToken, seedExpiresIn: tokens.ExpiresIn)
            .ConfigureAwait(false);
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
        _instanceVersion = null;
        _instanceOsVersion = null;

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }

    /// <summary>
    /// Resumes reporting after a <see cref="DisconnectAsync"/> and returns whether
    /// a saved session was available and rebuilt successfully.
    /// </summary>
    public Task<bool> ReconnectAsync(CancellationToken ct = default) => TryResumeAsync(ct);

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
    /// and removes all stored credentials and configuration. Home Assistant's
    /// Mobile App API has no registration-delete endpoint, so its device entry is
    /// deliberately left for the user to remove there. The user must sign in again
    /// afterwards.
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
        _ = _uriLauncher.LaunchAsync(new Uri(url));
    }

    public SystemStatus GetSystemStatus() => _status.GetStatus();

    /// <summary>
    /// Builds the REST, WebSocket and sensor stack and puts it on the air.
    /// Callers must hold the lifecycle lease.
    /// </summary>
    private async Task BuildAndStartAsync(
        CancellationToken ct, string? seedAccessToken = null, int seedExpiresIn = 0)
    {
        // A real session replaces the demo, wherever the demo was started: its
        // catalog must not stay behind and shadow the one built here, and the UI
        // must not keep claiming that nothing is being sent.
        ExitDemoMode();

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
            _webSocketFactory, url, tokenManager, config.WebhookId!,
            _loggerFactory.CreateLogger<HaWebSocketClient>());

        // The connection does not exist yet, and the lifecycle source has to be part
        // of the catalog that the connection will read from, so the final push is
        // resolved lazily rather than captured.
        ConnectionManager? live = null;
        // Named for the machine's power lifecycle, not the connection lifecycle
        // gate in _lifecycle. A new one is built per connection and released by
        // the catalog's Stop, so a route switch does not leak its signal hooks.
        var systemLifecycle = CreateLifecycleCoordinator(
            finalPush: token => live is null
                ? Task.FromResult(false)
                : live.SyncNowAsync(SensorReadContext.LifecycleTransition, token));

        var catalog = new SensorCatalog(
            _sensorSourceFactory(config, systemLifecycle, _lifecycleSignalSourceFactory()),
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
        connection.NotificationReceived += n => _notifications.Show(n);
        connection.RouteUnhealthy += _ => RequestRouteEvaluation(RouteTrigger.ConnectionFailed);

        // Only a batch Home Assistant actually accepted counts as delivery; anything
        // else leaves the transition recorded locally for the next successful sync.
        connection.SyncSucceeded += _ => systemLifecycle.ReportDelivered();
        live = connection;
        _connection = connection;
        var network = _network.GetCurrent();
        _lastNetwork = network;
        connection.SetNetworkAvailable(network.Kind != NetworkKind.Offline);
        connection.Start();

        // Sources push immediately on change; steady state stays at one batch per sync.
        catalog.Start(() => connection.RequestSync());
    }

    private string ActiveUrl(ServerConfig config) => _supervisor?.ActiveUrl ?? config.BaseUrl;

    /// <summary>
    /// Named for the machine's power lifecycle, not the connection lifecycle gate
    /// in <c>_lifecycle</c>. A new one is built per connection and released by the
    /// catalog's Stop, so a route switch does not leak its signal hooks. Demo mode
    /// passes no final push: there is nowhere to push to.
    /// </summary>
    private LifecycleCoordinator CreateLifecycleCoordinator(
        Func<CancellationToken, Task<bool>>? finalPush = null) =>
        new(_lifecycleJournalFactory(),
            finalPush,
            finalPushTimeout: FinalLifecyclePushTimeout,
            log: _loggerFactory.CreateLogger<LifecycleCoordinator>());

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

    /// <summary>
    /// Records Home Assistant's own device id for this registration the first time
    /// it is seen. It is what later proves a second address is the same instance.
    /// </summary>
    private async Task LearnInstanceIdentityAsync(
        HomeAssistantClient client, ServerConfig config, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(config.WebhookId))
            return;

        try
        {
            var info = await client.GetInstanceInfoAsync(config.WebhookId, ct).ConfigureAwait(false);
            if (info?.DeviceId is not null)
            {
                _instanceVersion = info.Version;

                if (string.IsNullOrEmpty(config.InstanceDeviceId))
                {
                    config.InstanceDeviceId = info.DeviceId;
                    _settings.Save(config);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            // Identity is a nicety at startup; the validation flow re-reads it.
        }

        // OS version requires the Supervisor API (only HA OS installs).
        try
        {
            _instanceOsVersion = await client.GetOsVersionAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            // Non-fatal: not all installs have a Supervisor.
        }
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
}
