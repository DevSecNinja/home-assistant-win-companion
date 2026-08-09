using WindowsCompanion.Core.HomeAssistant;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Abstractions;
using WindowsCompanion.Core.Sensors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WindowsCompanion.Core.App;

/// <summary>
/// Owns the live connection to Home Assistant: keeps the WebSocket alive with
/// exponential backoff, runs periodic sensor syncs, and surfaces state changes
/// and notifications. Auth failures are terminal (stop retrying) per the spec.
/// </summary>
public sealed class ConnectionManager : IAsyncDisposable
{
    private static readonly TimeSpan BackoffCap = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan BackoffBase = TimeSpan.FromSeconds(1);

    private readonly HaWebSocketClient _ws;
    private readonly SensorSyncService _sensors;
    private readonly string _webhookId;
    private readonly TimeSpan _syncInterval;
    private readonly ILogger<ConnectionManager> _log;
    private readonly Random _jitter = new();
    private readonly IClock _clock;

    private CancellationTokenSource? _cts;
    private Task? _wsLoop;
    private Task? _syncLoop;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    /// <summary>
    /// When sensor states were last pushed to Home Assistant successfully. Surfaced
    /// in the companion's own UI so the user can tell at a glance that it is alive.
    /// </summary>
    public DateTimeOffset? LastSyncedAt { get; private set; }

    /// <summary>Message from the most recent sync failure, for troubleshooting.</summary>
    public string? LastError { get; private set; }

    public DateTimeOffset? LastErrorAt { get; private set; }

    /// <summary>Sync failures since the last success; resets to zero on success.</summary>
    public int ConsecutiveFailures { get; private set; }

    /// <summary>
    /// Which of the configured addresses this connection uses. Null for installs
    /// that still have a single unclassified address.
    /// </summary>
    public RouteKind? Route { get; }

    /// <summary>
    /// Raised when this connection has failed often enough that another address is
    /// worth trying. The supervisor decides whether one exists; this only reports.
    /// </summary>
    public event Action<RouteKind?>? RouteUnhealthy;

    /// <summary>
    /// Failures tolerated before a route is called into question. Two means one
    /// transient hiccup is absorbed, but a server that has moved is noticed within
    /// a couple of sync intervals rather than after an hour of backoff.
    /// </summary>
    public const int FailoverFailureThreshold = 2;

    /// <summary>Reconnect attempts tolerated before the route is called into question.</summary>
    public const int FailoverReconnectThreshold = 2;

    public TimeSpan SyncInterval => _syncInterval;

    /// <summary>
    /// Healthy means connected and reporting on schedule. A missed sync window is
    /// the signal that matters: the socket can look fine while pushes are failing.
    /// </summary>
    public bool IsHealthy =>
        State == ConnectionState.Connected
        && ConsecutiveFailures == 0
        && LastSyncedAt is not null
        && _clock.UtcNow - LastSyncedAt.Value < _syncInterval * 2.5;

    public event Action<ConnectionState>? StateChanged;
    public event Action<NotificationMessage>? NotificationReceived;

    /// <summary>
    /// Raised after Home Assistant accepted a sensor batch. Lets a caller treat a
    /// push as delivered only when it actually was, rather than when it was sent.
    /// </summary>
    public event Action<SensorReadContext>? SyncSucceeded;

    public ConnectionManager(
        HaWebSocketClient ws,
        SensorSyncService sensors,
        string webhookId,
        TimeSpan? syncInterval = null,
        IClock? clock = null,
        ILogger<ConnectionManager>? log = null,
        RouteKind? route = null)
    {
        _ws = ws ?? throw new ArgumentNullException(nameof(ws));
        _sensors = sensors ?? throw new ArgumentNullException(nameof(sensors));
        _webhookId = webhookId ?? throw new ArgumentNullException(nameof(webhookId));
        _syncInterval = syncInterval ?? TimeSpan.FromSeconds(60);
        _clock = clock ?? new SystemClock();
        _log = log ?? NullLogger<ConnectionManager>.Instance;
        Route = route;
        _ws.NotificationReceived += n => NotificationReceived?.Invoke(n);
    }

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        SetState(ConnectionState.Connecting);
        _wsLoop = Task.Run(() => WebSocketLoopAsync(_cts.Token));
        _syncLoop = Task.Run(() => SyncLoopAsync(_cts.Token));
    }

    private async Task WebSocketLoopAsync(CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                SetState(attempt == 0 ? ConnectionState.Connecting : ConnectionState.Reconnecting);
                await _ws.RunAsync(ct).ConfigureAwait(false); // completes when the socket closes
                attempt = 0; // clean close -> reset backoff
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (HomeAssistantAuthException ex)
            {
                _log.LogError(ex, "Authentication failed; stopping reconnection.");
                SetState(ConnectionState.AuthError);
                return;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "WebSocket connection error; will retry.");
            }

            if (ct.IsCancellationRequested) return;
            SetState(ConnectionState.Reconnecting);
            if (attempt + 1 >= FailoverReconnectThreshold) RouteUnhealthy?.Invoke(Route);
            await Task.Delay(NextBackoff(attempt++), ct).ConfigureAwait(false);
        }
    }

    private async Task SyncLoopAsync(CancellationToken ct)
    {
        // Immediate first sync, then on the configured interval.
        while (!ct.IsCancellationRequested)
        {
            await SyncOnceAsync(SensorReadContext.Periodic, ct).ConfigureAwait(false);
            try
            {
                await Task.Delay(_syncInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Runs a single sensor sync now (e.g. on power-state change) and reports
    /// whether Home Assistant accepted it. <paramref name="ct"/> lets a caller put
    /// its own deadline on the attempt, which the lifecycle final push depends on.
    /// </summary>
    public Task<bool> SyncNowAsync(SensorReadContext? context = null, CancellationToken ct = default)
    {
        var own = _cts?.Token ?? CancellationToken.None;
        if (!ct.CanBeCanceled)
            return SyncOnceAsync(context ?? SensorReadContext.StateChange, own);

        return SyncWithDeadlineAsync(context ?? SensorReadContext.StateChange, own, ct);
    }

    private async Task<bool> SyncWithDeadlineAsync(
        SensorReadContext context, CancellationToken own, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(own, ct);
        return await SyncOnceAsync(context, linked.Token).ConfigureAwait(false);
    }

    private async Task<bool> SyncOnceAsync(SensorReadContext context, CancellationToken ct)
    {
        if (State == ConnectionState.AuthError) return false;
        try
        {
            await _sensors.SyncAsync(_webhookId, context, ct).ConfigureAwait(false);
            LastSyncedAt = _clock.UtcNow;
            ConsecutiveFailures = 0;
            LastError = null;
            _log.LogDebug("Sensor sync succeeded ({Reason}).", context.Reason);
            if (State is ConnectionState.Connecting or ConnectionState.Reconnecting)
                SetState(ConnectionState.Connected);
            SyncSucceeded?.Invoke(context);
            return true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ConsecutiveFailures++;
            LastError = ex.Message;
            LastErrorAt = _clock.UtcNow;
            _log.LogWarning(ex, "Sensor sync failed ({Reason}), failure #{Count}.",
                context.Reason, ConsecutiveFailures);
            if (ConsecutiveFailures >= FailoverFailureThreshold) RouteUnhealthy?.Invoke(Route);
        }

        return false;
    }

    internal TimeSpan NextBackoff(int attempt)
    {
        var exp = Math.Min(BackoffCap.TotalSeconds, BackoffBase.TotalSeconds * Math.Pow(2, attempt));
        var jitter = _jitter.NextDouble() * 0.5 * exp; // up to +50%
        return TimeSpan.FromSeconds(Math.Min(BackoffCap.TotalSeconds, exp + jitter));
    }

    private void SetState(ConnectionState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(state);
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is null) return;
        await _cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(_wsLoop ?? Task.CompletedTask, _syncLoop ?? Task.CompletedTask).ConfigureAwait(false);
        }
        catch { /* ignore shutdown races */ }
        _cts.Dispose();
        _cts = null;
    }
}

