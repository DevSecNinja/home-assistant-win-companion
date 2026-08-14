using Microsoft.Extensions.Logging;
using WindowsCompanion.Core.App;
using WindowsCompanion.Core.HomeAssistant;
using WindowsCompanion.Core.Models;

namespace WindowsCompanion_App;

public sealed partial class AppController
{
    /// <summary>The saved connection settings, as the settings UI edits them.</summary>
    public ConnectionSettingsDraft ConnectionSettings
    {
        get
        {
            var config = _config;
            if (config is null) return new ConnectionSettingsDraft();

            return new ConnectionSettingsDraft
            {
                PrimaryUrl = config.BaseUrl,
                UseSeparateUrls = config.UseSeparateUrls,
                InternalUrl = config.InternalUrl,
                ExternalUrl = config.ExternalUrl,
                Mode = config.ConnectionMode,
                TrustedNetworks = new TrustedNetworkSettings
                {
                    Cidrs = [.. config.TrustedNetworks.Cidrs],
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
        _supervisor = null;
        var activeUrl = config.UseSeparateUrls ? null : config.BaseUrl;

        try
        {
            await PrepareRouteAsync(RouteTrigger.UserRequested, lease.Token).ConfigureAwait(false);
            activeUrl ??= _supervisor?.ActiveUrl;
            if (!string.Equals(activeUrl, previous.BaseUrl, StringComparison.OrdinalIgnoreCase)
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
        _connection?.RequestImmediateRetry();
        RouteChanged?.Invoke();
    }

    private static (string BaseUrl, bool Separate, string? Internal, string? External, ConnectionMode Mode,
        TrustedNetworkSettings Trusted, bool Pending) Snapshot(ServerConfig config) =>
        (config.BaseUrl, config.UseSeparateUrls, config.InternalUrl, config.ExternalUrl, config.ConnectionMode,
            config.TrustedNetworks, config.RouteAssignmentPending);

    private static void Restore(
        ServerConfig config,
        (string BaseUrl, bool Separate, string? Internal, string? External, ConnectionMode Mode,
            TrustedNetworkSettings Trusted, bool Pending) snapshot)
    {
        config.BaseUrl = snapshot.BaseUrl;
        config.UseSeparateUrls = snapshot.Separate;
        config.InternalUrl = snapshot.Internal;
        config.ExternalUrl = snapshot.External;
        config.ConnectionMode = snapshot.Mode;
        config.TrustedNetworks = snapshot.Trusted;
        config.RouteAssignmentPending = snapshot.Pending;
    }

    private RouteSupervisor CreateSupervisor(ServerConfig config)
    {
        var supervisor = new RouteSupervisor(
            config, _probe, log: _loggerFactory.CreateLogger<RouteSupervisor>());
        supervisor.RouteActivated += _ => RouteChanged?.Invoke();
        return supervisor;
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

        if (!config.UseSeparateUrls || config.ConfiguredRoutes().Count == 0) return;

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
        var previous = Interlocked.Exchange(ref _networkSettle, settle);
        previous?.Cancel();
        if (Volatile.Read(ref _disposeStarted) != 0) settle.Cancel();

        _ = SettleNetworkChangeAsync(settle);
    }

    private async Task SettleNetworkChangeAsync(CancellationTokenSource settle)
    {
        try
        {
            await Task.Delay(NetworkSettleDelay, settle.Token).ConfigureAwait(false);
            var network = _network.GetCurrent();
            var previous = _lastNetwork;
            if (previous is not null && previous.HasSameRoutingProfile(network)) return;

            _lastNetwork = network;
            await EvaluateRouteAsync(RouteTrigger.NetworkChanged, settle.Token).ConfigureAwait(false);
            settle.Token.ThrowIfCancellationRequested();

            var connection = _connection;
            if (connection is null) return;
            var available = network.Kind != NetworkKind.Offline;
            connection.SetNetworkAvailable(available);
            if (available) connection.RequestImmediateRetry();
        }
        catch (OperationCanceledException) when (settle.IsCancellationRequested)
        {
        }
        finally
        {
            Interlocked.CompareExchange(ref _networkSettle, null, settle);
            settle.Dispose();
        }
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
        if (!config.UseSeparateUrls || config.ConfiguredRoutes().Count == 0) return;

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
}
