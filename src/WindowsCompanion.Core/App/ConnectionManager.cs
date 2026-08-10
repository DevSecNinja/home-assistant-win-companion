using System.Threading.Channels;
using WindowsCompanion.Core.Abstractions;
using WindowsCompanion.Core.HomeAssistant;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WindowsCompanion.Core.App;

public sealed record ConnectionRetryOptions
{
    public TimeSpan InitialReconnectDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaximumReconnectDelay { get; init; } = TimeSpan.FromMinutes(1);
    public double MaximumJitterRatio { get; init; } = 0.2;
    public TimeSpan StableConnectionPeriod { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan MaximumSyncRetryDelay { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan OfflineRetryDelay { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan RepetitiveLogInterval { get; init; } = TimeSpan.FromMinutes(15);
}

/// <summary>
/// Owns the live connection to Home Assistant. Reconnects and periodic sensor
/// pushes each have one loop, bounded backoff, and cancellation-aware one-shot
/// wakeups, so an outage cannot grow a queue of work.
/// </summary>
public sealed class ConnectionManager : IAsyncDisposable
{
    private readonly HaWebSocketClient _ws;
    private readonly SensorSyncService _sensors;
    private readonly string _webhookId;
    private readonly TimeSpan _syncInterval;
    private readonly ILogger<ConnectionManager> _log;
    private readonly Func<double> _jitter;
    private readonly IClock _clock;
    private readonly ConnectionRetryOptions _retry;
    private readonly Channel<byte> _syncSignal = CreateSignal();
    private readonly SemaphoreSlim _syncAttemptGate = new(1, 1);
    private readonly Lock _wakeGate = new();

    private CancellationTokenSource? _cts;
    private Task? _wsLoop;
    private Task? _syncLoop;
    private TaskCompletionSource? _reconnectWakeup;
    private TaskCompletionSource? _syncRetryWakeup;
    private DateTimeOffset _nextWebSocketWarningAt = DateTimeOffset.MinValue;
    private DateTimeOffset _nextSyncWarningAt = DateTimeOffset.MinValue;
    private int _syncSignalPending;
    private int _networkAvailable = 1;
    private int _webSocketRouteUnhealthyRaised;
    private int _syncRouteUnhealthyRaised;
    private long _authenticatedAtTimestamp = long.MinValue;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    public DateTimeOffset? LastSyncedAt { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset? LastErrorAt { get; private set; }

    public int ConsecutiveFailures { get; private set; }

    public RouteKind? Route { get; }

    public event Action<RouteKind?>? RouteUnhealthy;

    public const int FailoverFailureThreshold = 2;
    public const int FailoverReconnectThreshold = 2;

    public TimeSpan SyncInterval => _syncInterval;

    public bool IsHealthy =>
        State == ConnectionState.Connected
        && ConsecutiveFailures == 0
        && LastSyncedAt is not null
        && _clock.UtcNow - LastSyncedAt.Value < _syncInterval * 2.5;

    internal bool HasRunningLoops =>
        _wsLoop is { IsCompleted: false } || _syncLoop is { IsCompleted: false };

    public event Action<ConnectionState>? StateChanged;
    public event Action<NotificationMessage>? NotificationReceived;
    public event Action<SensorReadContext>? SyncSucceeded;

    public ConnectionManager(
        HaWebSocketClient ws,
        SensorSyncService sensors,
        string webhookId,
        TimeSpan? syncInterval = null,
        IClock? clock = null,
        ILogger<ConnectionManager>? log = null,
        RouteKind? route = null,
        ConnectionRetryOptions? retryOptions = null,
        Func<double>? jitter = null)
    {
        _ws = ws ?? throw new ArgumentNullException(nameof(ws));
        _sensors = sensors ?? throw new ArgumentNullException(nameof(sensors));
        _webhookId = webhookId ?? throw new ArgumentNullException(nameof(webhookId));
        _syncInterval = syncInterval ?? TimeSpan.FromSeconds(60);
        _clock = clock ?? new SystemClock();
        _log = log ?? NullLogger<ConnectionManager>.Instance;
        _retry = retryOptions ?? new ConnectionRetryOptions();
        _jitter = jitter ?? Random.Shared.NextDouble;
        Route = route;
        ValidateOptions(_retry, _syncInterval);
        _ws.NotificationReceived += n => NotificationReceived?.Invoke(n);
        _ws.Authenticated += OnWebSocketAuthenticated;
    }

    public void Start()
    {
        if (_cts is not null) return;
        DrainSignal(_syncSignal, ref _syncSignalPending);
        _cts = new CancellationTokenSource();
        SetState(ConnectionState.Connecting);
        _wsLoop = Task.Run(() => WebSocketLoopAsync(_cts.Token));
        _syncLoop = Task.Run(() => SyncLoopAsync(_cts.Token));
    }

    /// <summary>
    /// Records whether Windows currently has a usable network. Going online wakes
    /// one pending reconnect; repeated events coalesce in the bounded signal.
    /// </summary>
    public void SetNetworkAvailable(bool available)
    {
        var previous = Interlocked.Exchange(ref _networkAvailable, available ? 1 : 0);
        if (available && previous == 0) RequestImmediateRetry();
    }

    /// <summary>
    /// Bypasses the current reconnect and sync delays once. It never starts another
    /// attempt; if work is in flight, the bounded signal applies after that attempt.
    /// </summary>
    public bool RequestImmediateRetry()
    {
        lock (_wakeGate)
        {
            var reconnect = _reconnectWakeup?.TrySetResult() == true;
            var sync = _syncRetryWakeup?.TrySetResult() == true;
            return reconnect || sync;
        }
    }

    /// <summary>
    /// Coalesces noisy sensor events into one immediate push while healthy. During
    /// an outage the periodic backoff owns retries, so events cannot grow a queue.
    /// </summary>
    public bool RequestSync()
    {
        if (_cts is null || ConsecutiveFailures > 0) return false;
        return TrySignal(_syncSignal, ref _syncSignalPending);
    }

    private async Task WebSocketLoopAsync(CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                SetState(attempt == 0 ? ConnectionState.Connecting : ConnectionState.Reconnecting);
                Interlocked.Exchange(ref _authenticatedAtTimestamp, long.MinValue);
                await _ws.RunAsync(ct).ConfigureAwait(false);
                ResetReconnectAfterStableConnection(ref attempt);
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
                ResetReconnectAfterStableConnection(ref attempt);
                LogWebSocketFailure(ex);
            }

            if (ct.IsCancellationRequested) return;

            SetState(ConnectionState.Reconnecting);
            attempt++;
            if (attempt >= FailoverReconnectThreshold
                && Interlocked.Exchange(ref _webSocketRouteUnhealthyRaised, 1) == 0)
            {
                RouteUnhealthy?.Invoke(Route);
            }

            var delay = Volatile.Read(ref _networkAvailable) == 0
                ? _retry.OfflineRetryDelay
                : NextBackoff(attempt - 1);
            await WaitForRetryAsync(delay, ct).ConfigureAwait(false);
        }
    }

    private async Task WaitForRetryAsync(TimeSpan delay, CancellationToken ct)
    {
        var wakeup = NewWakeup();
        lock (_wakeGate) _reconnectWakeup = wakeup;
        try
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var delayTask = _clock.DelayAsync(delay, wait.Token);
            var completed = await Task.WhenAny(delayTask, wakeup.Task).ConfigureAwait(false);
            await completed.ConfigureAwait(false);
            await wait.CancelAsync().ConfigureAwait(false);
            await ObserveCanceledDelayAsync(delayTask, wait.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_wakeGate)
            {
                if (ReferenceEquals(_reconnectWakeup, wakeup)) _reconnectWakeup = null;
            }
        }
    }

    private async Task SyncLoopAsync(CancellationToken ct)
    {
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            var succeeded = await SyncOnceAsync(SensorReadContext.Periodic, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return;

            failures = succeeded ? 0 : failures + 1;
            var delay = Volatile.Read(ref _networkAvailable) == 0
                ? _retry.OfflineRetryDelay
                : succeeded
                    ? _syncInterval
                    : NextSyncBackoff(failures - 1);

            if (!succeeded)
            {
                DrainSignal(_syncSignal, ref _syncSignalPending);
                await WaitForSyncRecoveryAsync(delay, ct).ConfigureAwait(false);
                continue;
            }

            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var delayTask = _clock.DelayAsync(delay, wait.Token);
            var signalTask = _syncSignal.Reader.ReadAsync(wait.Token).AsTask();
            var completed = await Task.WhenAny(delayTask, signalTask).ConfigureAwait(false);
            await completed.ConfigureAwait(false);
            await wait.CancelAsync().ConfigureAwait(false);
            await ObserveCanceledWaitAsync(delayTask, signalTask, wait.Token).ConfigureAwait(false);
            DrainSignal(_syncSignal, ref _syncSignalPending);
        }
    }

    private async Task WaitForSyncRecoveryAsync(TimeSpan delay, CancellationToken ct)
    {
        var wakeup = NewWakeup();
        lock (_wakeGate) _syncRetryWakeup = wakeup;
        try
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var delayTask = _clock.DelayAsync(delay, wait.Token);
            var completed = await Task.WhenAny(delayTask, wakeup.Task).ConfigureAwait(false);
            await completed.ConfigureAwait(false);
            await wait.CancelAsync().ConfigureAwait(false);
            await ObserveCanceledDelayAsync(delayTask, wait.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_wakeGate)
            {
                if (ReferenceEquals(_syncRetryWakeup, wakeup)) _syncRetryWakeup = null;
            }
        }
    }

    private void OnWebSocketAuthenticated()
    {
        Interlocked.Exchange(ref _authenticatedAtTimestamp, _clock.GetTimestamp());
        if (ConsecutiveFailures <= 0) return;
        lock (_wakeGate) _syncRetryWakeup?.TrySetResult();
    }

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
        await _syncAttemptGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await SyncOnceCoreAsync(context, ct).ConfigureAwait(false);
        }
        finally
        {
            _syncAttemptGate.Release();
        }
    }

    private async Task<bool> SyncOnceCoreAsync(SensorReadContext context, CancellationToken ct)
    {
        if (State == ConnectionState.AuthError) return false;
        try
        {
            await _sensors.SyncAsync(_webhookId, context, ct).ConfigureAwait(false);
            LastSyncedAt = _clock.UtcNow;
            ConsecutiveFailures = 0;
            LastError = null;
            _nextSyncWarningAt = DateTimeOffset.MinValue;
            Interlocked.Exchange(ref _syncRouteUnhealthyRaised, 0);
            _log.LogDebug("Sensor sync succeeded ({Reason}).", context.Reason);
            if (State is ConnectionState.Connecting or ConnectionState.Reconnecting)
                SetState(ConnectionState.Connected);
            SyncSucceeded?.Invoke(context);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }

        catch (Exception ex)
        {
            ConsecutiveFailures++;
            LastError = ex.Message;
            LastErrorAt = _clock.UtcNow;
            LogSyncFailure(ex, context);
            if (ConsecutiveFailures >= FailoverFailureThreshold
                && Interlocked.Exchange(ref _syncRouteUnhealthyRaised, 1) == 0)
            {
                RouteUnhealthy?.Invoke(Route);
            }
        }

        return false;
    }

    private void ResetReconnectAfterStableConnection(ref int attempt)
    {
        var authenticatedAt = Interlocked.Read(ref _authenticatedAtTimestamp);
        if (authenticatedAt == long.MinValue
            || _clock.GetElapsedTime(authenticatedAt) < _retry.StableConnectionPeriod)
        {
            return;
        }

        attempt = 0;
        Interlocked.Exchange(ref _webSocketRouteUnhealthyRaised, 0);
        _nextWebSocketWarningAt = DateTimeOffset.MinValue;
    }

    internal TimeSpan NextBackoff(int attempt, double? jitterSample = null)
    {
        var multiplier = Math.Pow(2, Math.Max(0, attempt));
        var exponential = Math.Min(
            _retry.MaximumReconnectDelay.TotalMilliseconds,
            _retry.InitialReconnectDelay.TotalMilliseconds * multiplier);
        var sample = Math.Clamp(jitterSample ?? _jitter(), 0, 1);
        var withJitter = exponential * (1 + sample * _retry.MaximumJitterRatio);
        return TimeSpan.FromMilliseconds(Math.Min(
            _retry.MaximumReconnectDelay.TotalMilliseconds,
            withJitter));
    }

    internal TimeSpan NextSyncBackoff(int attempt)
    {
        var multiplier = Math.Pow(2, Math.Max(0, attempt));
        return TimeSpan.FromMilliseconds(Math.Min(
            _retry.MaximumSyncRetryDelay.TotalMilliseconds,
            _syncInterval.TotalMilliseconds * multiplier));
    }

    private void LogWebSocketFailure(Exception ex)
    {
        if (_clock.UtcNow >= _nextWebSocketWarningAt)
        {
            _nextWebSocketWarningAt = _clock.UtcNow + _retry.RepetitiveLogInterval;
            _log.LogWarning("WebSocket connection error; retrying with backoff ({ErrorType}).",
                ex.GetType().Name);
        }
        else
        {
            _log.LogDebug("WebSocket connection remains unavailable ({ErrorType}).",
                ex.GetType().Name);
        }
    }

    private void LogSyncFailure(Exception ex, SensorReadContext context)
    {
        if (_clock.UtcNow >= _nextSyncWarningAt)
        {
            _nextSyncWarningAt = _clock.UtcNow + _retry.RepetitiveLogInterval;
            _log.LogWarning("Sensor sync failed ({Reason}), failure #{Count} ({ErrorType}).",
                context.Reason, ConsecutiveFailures, ex.GetType().Name);
        }
        else
        {
            _log.LogDebug("Sensor sync remains unavailable ({Reason}), failure #{Count}.",
                context.Reason, ConsecutiveFailures);
        }
    }

    private void SetState(ConnectionState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(state);
    }

    public async ValueTask DisposeAsync()
    {
        var cts = _cts;
        if (cts is null) return;

        await cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(_wsLoop ?? Task.CompletedTask, _syncLoop ?? Task.CompletedTask)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        finally
        {
            cts.Dispose();
            _cts = null;
            _wsLoop = null;
            _syncLoop = null;
            lock (_wakeGate)
            {
                _reconnectWakeup = null;
                _syncRetryWakeup = null;
            }
            DrainSignal(_syncSignal, ref _syncSignalPending);
            SetState(ConnectionState.Disconnected);
        }
    }

    private static Channel<byte> CreateSignal() =>
        Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    private static bool TrySignal(Channel<byte> signal, ref int pending)
    {
        if (Interlocked.CompareExchange(ref pending, 1, 0) != 0) return false;
        if (signal.Writer.TryWrite(0)) return true;
        Volatile.Write(ref pending, 0);
        return false;
    }

    private static void DrainSignal(Channel<byte> signal, ref int pending)
    {
        while (signal.Reader.TryRead(out _)) { }
        Volatile.Write(ref pending, 0);
    }

    private static TaskCompletionSource NewWakeup() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task ObserveCanceledDelayAsync(
        Task delayTask, CancellationToken waitToken)
    {
        try
        {
            await delayTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (waitToken.IsCancellationRequested)
        {
        }
    }

    private static async Task ObserveCanceledWaitAsync(
        Task delayTask, Task signalTask, CancellationToken waitToken)
    {
        try
        {
            await Task.WhenAll(delayTask, signalTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (waitToken.IsCancellationRequested)
        {
        }
    }

    private static void ValidateOptions(ConnectionRetryOptions options, TimeSpan syncInterval)
    {
        if (syncInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(syncInterval));
        if (options.InitialReconnectDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.InitialReconnectDelay));
        if (options.MaximumReconnectDelay < options.InitialReconnectDelay)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumReconnectDelay));
        if (!double.IsFinite(options.MaximumJitterRatio)
            || options.MaximumJitterRatio is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumJitterRatio));
        if (options.StableConnectionPeriod < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.StableConnectionPeriod));
        if (options.MaximumSyncRetryDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumSyncRetryDelay));
        if (options.OfflineRetryDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.OfflineRetryDelay));
        if (options.RepetitiveLogInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.RepetitiveLogInterval));
    }
}
