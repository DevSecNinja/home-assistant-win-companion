using HaCompanion.Core.HomeAssistant;
using HaCompanion.Core.Models;
using HaCompanion.Core.Sensors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HaCompanion.Core.App;

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

    private CancellationTokenSource? _cts;
    private Task? _wsLoop;
    private Task? _syncLoop;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    public event Action<ConnectionState>? StateChanged;
    public event Action<NotificationMessage>? NotificationReceived;

    public ConnectionManager(
        HaWebSocketClient ws,
        SensorSyncService sensors,
        string webhookId,
        TimeSpan? syncInterval = null,
        ILogger<ConnectionManager>? log = null)
    {
        _ws = ws ?? throw new ArgumentNullException(nameof(ws));
        _sensors = sensors ?? throw new ArgumentNullException(nameof(sensors));
        _webhookId = webhookId ?? throw new ArgumentNullException(nameof(webhookId));
        _syncInterval = syncInterval ?? TimeSpan.FromSeconds(60);
        _log = log ?? NullLogger<ConnectionManager>.Instance;
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
            await Task.Delay(NextBackoff(attempt++), ct).ConfigureAwait(false);
        }
    }

    private async Task SyncLoopAsync(CancellationToken ct)
    {
        // Immediate first sync, then on the configured interval.
        while (!ct.IsCancellationRequested)
        {
            await SyncOnceAsync(ct).ConfigureAwait(false);
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

    /// <summary>Runs a single sensor sync now (e.g. on power-state change).</summary>
    public Task SyncNowAsync() => SyncOnceAsync(_cts?.Token ?? CancellationToken.None);

    private async Task SyncOnceAsync(CancellationToken ct)
    {
        if (State == ConnectionState.AuthError) return;
        try
        {
            await _sensors.SyncAsync(_webhookId, ct).ConfigureAwait(false);
            if (State is ConnectionState.Connecting or ConnectionState.Reconnecting)
                SetState(ConnectionState.Connected);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Sensor sync failed.");
        }
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
