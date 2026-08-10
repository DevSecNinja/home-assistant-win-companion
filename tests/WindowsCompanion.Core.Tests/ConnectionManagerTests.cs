using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using WindowsCompanion.Core.Abstractions;
using WindowsCompanion.Core.App;
using WindowsCompanion.Core.HomeAssistant;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;

namespace WindowsCompanion.Core.Tests;

[Collection(AsyncLifecycleCollection.Name)]
public class ConnectionManagerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Auth_error_is_terminal_and_stops_reconnecting()
    {
        var clock = new ManualClock();
        var socket = new ScriptedSocket(
            """{"type":"auth_required"}""",
            """{"type":"auth_invalid"}""");
        var manager = CreateManager(() => socket, new FakeClient(), clock);
        var authError = StateReached(manager, ConnectionState.AuthError);

        manager.Start();
        await authError;
        clock.AdvanceBy(TimeSpan.FromDays(1));

        Assert.Equal(ConnectionState.AuthError, manager.State);
        Assert.Equal(1, socket.ConnectCount);
        await manager.DisposeAsync();
    }

    [Fact]
    public void Backoff_progression_jitter_and_cap_are_deterministic()
    {
        var manager = CreateManager(
            () => new BlockingSocket(), new FakeClient(), new ManualClock());

        Assert.Equal(TimeSpan.FromSeconds(1), manager.NextBackoff(0, 0));
        Assert.Equal(TimeSpan.FromSeconds(1.2), manager.NextBackoff(0, 1));
        Assert.Equal(TimeSpan.FromSeconds(8.8), manager.NextBackoff(3, 0.5));
        Assert.Equal(TimeSpan.FromSeconds(60), manager.NextBackoff(20, 0));
        Assert.Equal(TimeSpan.FromSeconds(60), manager.NextBackoff(20, 1));
    }

    [Fact]
    public void Non_finite_jitter_ratio_is_rejected_during_construction()
    {
        var retry = new ConnectionRetryOptions { MaximumJitterRatio = double.NaN };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateManager(() => new BlockingSocket(), new FakeClient(), new ManualClock(),
                retry: retry));
    }

    [Fact]
    public async Task Early_socket_closures_keep_increasing_backoff()
    {
        var clock = new ManualClock();
        var attempts = new AttemptFactory(_ => new ClosingSocket());
        var manager = CreateManager(attempts.Create, new FakeClient { BlockUpdates = true }, clock);

        manager.Start();
        await attempts.ReachedAsync(1);
        Assert.Equal(TimeSpan.FromSeconds(1), await clock.NextDelayAsync(IsReconnectDelay));

        clock.AdvanceBy(TimeSpan.FromSeconds(1));
        await attempts.ReachedAsync(2);
        Assert.Equal(TimeSpan.FromSeconds(2), await clock.NextDelayAsync(IsReconnectDelay));

        clock.AdvanceBy(TimeSpan.FromSeconds(2));
        await attempts.ReachedAsync(3);
        Assert.Equal(TimeSpan.FromSeconds(4), await clock.NextDelayAsync(IsReconnectDelay));

        await manager.DisposeAsync();
    }

    [Fact]
    public async Task Stable_authenticated_connection_resets_backoff()
    {
        var clock = new ManualClock();
        var stable = new AuthenticatedSocket();
        var attempts = new AttemptFactory(number => number switch
        {
            1 => new ThrowingSocket(),
            2 => stable,
            _ => new ThrowingSocket()
        });
        var manager = CreateManager(attempts.Create, new FakeClient { BlockUpdates = true }, clock);

        manager.Start();
        await attempts.ReachedAsync(1);
        Assert.Equal(TimeSpan.FromSeconds(1), await clock.NextDelayAsync(IsReconnectDelay));
        clock.AdvanceBy(TimeSpan.FromSeconds(1));

        await attempts.ReachedAsync(2);
        await stable.Authenticated.Task.WaitAsync(Timeout);
        clock.AdvanceBy(TimeSpan.FromSeconds(30));
        stable.Fail();

        Assert.Equal(TimeSpan.FromSeconds(1), await clock.NextDelayAsync(IsReconnectDelay));
        await manager.DisposeAsync();
    }

    [Fact]
    public async Task Wall_clock_change_does_not_make_a_short_connection_stable()
    {
        var clock = new ManualClock();
        var shortLived = new AuthenticatedSocket();
        var attempts = new AttemptFactory(number => number switch
        {
            1 => new ThrowingSocket(),
            2 => shortLived,
            _ => new ThrowingSocket()
        });
        var manager = CreateManager(attempts.Create, new FakeClient { BlockUpdates = true }, clock);

        manager.Start();
        await attempts.ReachedAsync(1);
        await clock.NextDelayAsync(IsReconnectDelay);
        clock.AdvanceBy(TimeSpan.FromSeconds(1));

        await attempts.ReachedAsync(2);
        await shortLived.Authenticated.Task.WaitAsync(Timeout);
        clock.AdjustUtcBy(TimeSpan.FromHours(1));
        shortLived.Fail();

        Assert.Equal(TimeSpan.FromSeconds(2), await clock.NextDelayAsync(IsReconnectDelay));
        await manager.DisposeAsync();
    }

    [Fact]
    public async Task Retry_requested_during_an_attempt_does_not_skip_a_future_delay()
    {
        var clock = new ManualClock();
        var first = new ControlledFailSocket();
        var attempts = new AttemptFactory(number =>
            number == 1 ? first : new ThrowingSocket());
        var manager = CreateManager(attempts.Create, new FakeClient { BlockUpdates = true }, clock);

        manager.Start();
        await first.Entered.Task.WaitAsync(Timeout);
        Assert.False(manager.RequestImmediateRetry());
        first.Fail();

        Assert.Equal(TimeSpan.FromSeconds(1), await clock.NextDelayAsync(IsReconnectDelay));
        Assert.Equal(1, attempts.Count);
        clock.AdvanceBy(TimeSpan.FromSeconds(1));
        await attempts.ReachedAsync(2);

        await manager.DisposeAsync();
    }

    [Fact]
    public async Task Manual_retry_bypasses_one_delay_and_duplicate_requests_coalesce()
    {
        var clock = new ManualClock();
        var attempts = new AttemptFactory(_ => new ThrowingSocket());
        var manager = CreateManager(attempts.Create, new FakeClient { BlockUpdates = true }, clock);

        manager.Start();
        await attempts.ReachedAsync(1);
        await clock.NextDelayAsync(IsReconnectDelay);

        var accepted = Enumerable.Range(0, 20)
            .Count(_ => manager.RequestImmediateRetry());

        Assert.Equal(1, accepted);
        await attempts.ReachedAsync(2);
        await clock.NextDelayAsync(IsReconnectDelay);
        Assert.Equal(2, attempts.Count);

        await manager.DisposeAsync();
    }

    [Fact]
    public async Task Offline_retry_is_low_cost_and_online_change_bypasses_it_once()
    {
        var clock = new ManualClock();
        var attempts = new AttemptFactory(_ => new ThrowingSocket());
        var manager = CreateManager(attempts.Create, new FakeClient { BlockUpdates = true }, clock);
        manager.SetNetworkAvailable(false);

        manager.Start();
        await attempts.ReachedAsync(1);
        Assert.Equal(TimeSpan.FromMinutes(5), await clock.NextDelayAsync(_ => true));

        manager.SetNetworkAvailable(true);
        manager.SetNetworkAvailable(true);
        await attempts.ReachedAsync(2);
        Assert.Equal(2, attempts.Count);

        await manager.DisposeAsync();
    }

    [Fact]
    public async Task Concurrent_events_never_overlap_connection_attempts()
    {
        var socket = new BlockingConnectSocket();
        var clock = new ManualClock();
        var manager = CreateManager(
            () => socket, new FakeClient { BlockUpdates = true }, clock);

        manager.Start();
        await socket.Entered.Task.WaitAsync(Timeout);

        await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            manager.RequestImmediateRetry();
            manager.SetNetworkAvailable(false);
            manager.SetNetworkAvailable(true);
        })));

        Assert.Equal(1, socket.MaximumConcurrentConnects);
        Assert.Equal(1, socket.ConnectCount);
        await manager.DisposeAsync();
    }

    [Fact]
    public async Task Prolonged_sync_outage_backs_off_coalesces_events_and_bounds_logs()
    {
        var clock = new ManualClock();
        var homeAssistant = new FakeClient { AlwaysFailUpdates = true };
        var logger = new ListLogger<ConnectionManager>();
        var manager = CreateManager(
            () => new BlockingSocket(),
            homeAssistant,
            clock,
            syncInterval: TimeSpan.FromSeconds(1),
            retry: new ConnectionRetryOptions
            {
                MaximumSyncRetryDelay = TimeSpan.FromSeconds(8),
                RepetitiveLogInterval = TimeSpan.FromMinutes(15)
            },
            logger: logger);
        var unhealthy = 0;
        manager.RouteUnhealthy += _ => unhealthy++;

        manager.Start();
        await homeAssistant.UpdatesReachedAsync(1);

        foreach (var expected in new[] { 1, 2, 4, 8, 8, 8 })
        {
            Assert.Equal(TimeSpan.FromSeconds(expected),
                await clock.NextDelayAsync(delay => delay <= TimeSpan.FromSeconds(8)));
            Assert.DoesNotContain(
                Enumerable.Range(0, 100).Select(_ => manager.RequestSync()),
                accepted => accepted);
            var nextUpdate = homeAssistant.UpdateCount + 1;
            clock.AdvanceBy(TimeSpan.FromSeconds(expected));
            await homeAssistant.UpdatesReachedAsync(nextUpdate);
        }

        Assert.Equal(1, unhealthy);
        Assert.Equal(1, logger.Count(LogLevel.Warning));
        Assert.InRange(homeAssistant.RequestCount, 7, 16);

        await manager.DisposeAsync();
        Assert.False(manager.HasRunningLoops);
        Assert.Equal(0, clock.PendingDelayCount);
    }

    [Fact]
    public async Task Manual_or_network_recovery_wakes_one_failed_sync_delay()
    {
        var clock = new ManualClock();
        var homeAssistant = new FakeClient { AlwaysFailUpdates = true };
        var manager = CreateManager(
            () => new BlockingSocket(),
            homeAssistant,
            clock,
            syncInterval: TimeSpan.FromMinutes(1));

        manager.Start();
        await homeAssistant.UpdatesReachedAsync(1);
        await clock.NextDelayAsync(delay => delay == TimeSpan.FromMinutes(1));

        var accepted = Enumerable.Range(0, 20)
            .Count(_ => manager.RequestImmediateRetry());

        Assert.Equal(1, accepted);
        await homeAssistant.UpdatesReachedAsync(2);
        Assert.Equal(2, homeAssistant.UpdateCount);

        await manager.DisposeAsync();
    }

    [Fact]
    public async Task Sensor_event_queued_during_failed_sync_is_discarded()
    {
        var clock = new ManualClock();
        var homeAssistant = new FakeClient { AlwaysFailUpdates = true };
        var releaseFirstUpdate = homeAssistant.HoldNextUpdate();
        var manager = CreateManager(
            () => new BlockingSocket(),
            homeAssistant,
            clock,
            syncInterval: TimeSpan.FromMinutes(1));

        manager.Start();
        await homeAssistant.UpdatesReachedAsync(1);
        Assert.True(manager.RequestSync());
        releaseFirstUpdate.TrySetResult();
        await clock.NextDelayAsync(delay => delay == TimeSpan.FromMinutes(1));

        Assert.True(manager.RequestImmediateRetry());
        await homeAssistant.UpdatesReachedAsync(2);
        await clock.NextDelayAsync(delay => delay == TimeSpan.FromMinutes(2));
        Assert.Equal(2, homeAssistant.UpdateCount);

        await manager.DisposeAsync();
    }

    [Fact]
    public async Task Prolonged_websocket_outage_caps_attempts_logs_and_failover_signal()
    {
        var clock = new ManualClock();
        var attempts = new AttemptFactory(_ => new ThrowingSocket());
        var logger = new ListLogger<ConnectionManager>();
        var manager = CreateManager(
            attempts.Create,
            new FakeClient { BlockUpdates = true },
            clock,
            logger: logger);
        var unhealthy = 0;
        manager.RouteUnhealthy += _ => unhealthy++;

        manager.Start();
        await attempts.ReachedAsync(1);

        for (var attempt = 1; attempt <= 20; attempt++)
        {
            var delay = await clock.NextDelayAsync(IsReconnectDelay);
            if (attempt >= 7) Assert.Equal(TimeSpan.FromMinutes(1), delay);
            clock.AdvanceBy(delay);
            await attempts.ReachedAsync(attempt + 1);
        }

        Assert.Equal(21, attempts.Count);
        Assert.Equal(1, unhealthy);
        Assert.Equal(2, logger.Count(LogLevel.Warning));

        await manager.DisposeAsync();
        Assert.False(manager.HasRunningLoops);
        Assert.Equal(0, clock.PendingDelayCount);
    }

    [Fact]
    public async Task Route_unhealthy_is_raised_once_per_sync_outage_and_resets_after_recovery()
    {
        var clock = new ManualClock();
        var homeAssistant = new FakeClient();
        var manager = CreateManager(
            () => new BlockingSocket(), homeAssistant, clock, route: RouteKind.Internal);
        var reported = new List<RouteKind?>();
        manager.RouteUnhealthy += reported.Add;
        var firstSync = SyncSucceeded(manager);

        manager.Start();
        await firstSync;

        homeAssistant.FailUpdates = 3;
        Assert.False(await manager.SyncNowAsync());
        Assert.False(await manager.SyncNowAsync());
        Assert.False(await manager.SyncNowAsync());
        Assert.Equal([RouteKind.Internal], reported);

        Assert.True(await manager.SyncNowAsync());
        homeAssistant.FailUpdates = 2;
        Assert.False(await manager.SyncNowAsync());
        Assert.False(await manager.SyncNowAsync());
        Assert.Equal([RouteKind.Internal, RouteKind.Internal], reported);

        await manager.DisposeAsync();
    }

    [Fact]
    public async Task Health_tracks_success_failure_and_staleness()
    {
        var clock = new ManualClock();
        var homeAssistant = new FakeClient();
        var manager = CreateManager(
            () => new BlockingSocket(),
            homeAssistant,
            clock,
            syncInterval: TimeSpan.FromMinutes(1));
        var firstSync = SyncSucceeded(manager);

        manager.Start();
        await firstSync;
        Assert.True(manager.IsHealthy);

        clock.AdvanceBy(TimeSpan.FromMinutes(3));
        Assert.False(manager.IsHealthy);

        homeAssistant.FailUpdates = 1;
        Assert.False(await manager.SyncNowAsync());
        Assert.Equal(1, manager.ConsecutiveFailures);

        Assert.True(await manager.SyncNowAsync());
        Assert.Equal(0, manager.ConsecutiveFailures);
        Assert.True(manager.IsHealthy);
        await manager.DisposeAsync();
    }

    [Fact]
    public async Task Shutdown_cancels_pending_reconnect_and_sync_delays()
    {
        var clock = new ManualClock();
        var attempts = new AttemptFactory(_ => new ThrowingSocket());
        var manager = CreateManager(attempts.Create, new FakeClient(), clock);

        manager.Start();
        await attempts.ReachedAsync(1);
        await clock.DelaysReachedAsync(2);

        await manager.DisposeAsync();

        Assert.False(manager.HasRunningLoops);
        Assert.Equal(ConnectionState.Disconnected, manager.State);
        Assert.Equal(0, clock.PendingDelayCount);
    }

    private static bool IsReconnectDelay(TimeSpan delay) =>
        delay <= TimeSpan.FromMinutes(1);

    private static Task StateReached(ConnectionManager manager, ConnectionState expected)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.StateChanged += state =>
        {
            if (state == expected) reached.TrySetResult();
        };
        return reached.Task.WaitAsync(Timeout);
    }

    private static Task SyncSucceeded(ConnectionManager manager)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.SyncSucceeded += _ => reached.TrySetResult();
        return reached.Task.WaitAsync(Timeout);
    }

    private static ConnectionManager CreateManager(
        Func<IHaSocket> socketFactory,
        FakeClient client,
        ManualClock clock,
        TimeSpan? syncInterval = null,
        RouteKind? route = null,
        ConnectionRetryOptions? retry = null,
        ILogger<ConnectionManager>? logger = null)
    {
        var webSocket = new HaWebSocketClient(
            socketFactory,
            "https://ha.local:8123",
            new StaticTokenProvider("token"),
            "hook");
        var catalog = new SensorCatalog(
            [new StubSource()],
            new SensorPreferences());
        var sensors = new SensorSyncService(client, catalog);
        return new ConnectionManager(
            webSocket,
            sensors,
            "hook",
            syncInterval ?? TimeSpan.FromHours(1),
            clock,
            logger,
            route,
            retry,
            jitter: () => 0);
    }

    private sealed class ManualClock : IClock
    {
        private readonly Lock _gate = new();
        private readonly List<DelayRequest> _pending = [];
        private readonly Channel<TimeSpan> _scheduled = Channel.CreateUnbounded<TimeSpan>();
        private int _scheduledCount;
        private long _timestampTicks;

        public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.UnixEpoch;

        public long GetTimestamp() => Interlocked.Read(ref _timestampTicks);

        public TimeSpan GetElapsedTime(long startingTimestamp) =>
            TimeSpan.FromTicks(Interlocked.Read(ref _timestampTicks) - startingTimestamp);

        public int PendingDelayCount
        {
            get { lock (_gate) return _pending.Count; }
        }

        public Task DelayAsync(TimeSpan delay, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            DelayRequest request;
            lock (_gate)
            {
                request = new DelayRequest(UtcNow + delay);
                _pending.Add(request);
            }

            request.Cancellation = ct.Register(() =>
            {
                lock (_gate) _pending.Remove(request);
                request.Completion.TrySetCanceled(ct);
            });

            Interlocked.Increment(ref _scheduledCount);
            _scheduled.Writer.TryWrite(delay);
            return request.Completion.Task;
        }

        public async Task<TimeSpan> NextDelayAsync(Func<TimeSpan, bool> predicate)
        {
            using var timeout = new CancellationTokenSource(Timeout);
            while (true)
            {
                var delay = await _scheduled.Reader.ReadAsync(timeout.Token);
                if (predicate(delay)) return delay;
            }
        }

        public async Task DelaysReachedAsync(int count)
        {
            using var timeout = new CancellationTokenSource(Timeout);
            while (Volatile.Read(ref _scheduledCount) < count)
                await _scheduled.Reader.ReadAsync(timeout.Token);
        }

        public void AdvanceBy(TimeSpan amount)
        {
            List<DelayRequest> ready;
            lock (_gate)
            {
                UtcNow += amount;
                Interlocked.Add(ref _timestampTicks, amount.Ticks);
                ready = _pending.Where(request => request.DueAt <= UtcNow).ToList();
                foreach (var request in ready) _pending.Remove(request);
            }

            foreach (var request in ready)
            {
                request.Cancellation.Dispose();
                request.Completion.TrySetResult();
            }
        }

        public void AdjustUtcBy(TimeSpan amount)
        {
            lock (_gate) UtcNow += amount;
        }

        private sealed class DelayRequest(DateTimeOffset dueAt)
        {
            public DateTimeOffset DueAt { get; } = dueAt;
            public TaskCompletionSource Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public CancellationTokenRegistration Cancellation { get; set; }
        }
    }

    private sealed class FakeClient : IHomeAssistantClient
    {
        private readonly Channel<int> _updates = Channel.CreateUnbounded<int>();
        private int _requestCount;
        private int _updateCount;
        private int _failUpdates;
        private TaskCompletionSource? _updateRelease;

        public bool AlwaysFailUpdates { get; set; }
        public bool BlockUpdates { get; set; }

        public int FailUpdates
        {
            get => Volatile.Read(ref _failUpdates);
            set => Volatile.Write(ref _failUpdates, value);
        }

        public int RequestCount => Volatile.Read(ref _requestCount);
        public int UpdateCount => Volatile.Read(ref _updateCount);

        public Task<bool> ValidateAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<DeviceRegistrationResponse> RegisterDeviceAsync(
            DeviceRegistrationRequest request, CancellationToken ct = default) =>
            Task.FromResult(new DeviceRegistrationResponse { WebhookId = "hook" });

        public Task UpdateRegistrationAsync(
            string webhookId, DeviceRegistrationRequest request, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task RegisterSensorAsync(
            string webhookId, Sensor sensor, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _requestCount);
            return Task.CompletedTask;
        }

        public async Task UpdateSensorsAsync(
            string webhookId, IReadOnlyList<Sensor> sensors, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _requestCount);
            var count = Interlocked.Increment(ref _updateCount);
            _updates.Writer.TryWrite(count);

            var release = Interlocked.Exchange(ref _updateRelease, null);
            if (release is not null)
                await release.Task.WaitAsync(ct);

            if (BlockUpdates)
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);

            if (AlwaysFailUpdates || TryConsumeFailure())
                throw new HttpRequestException("offline");
        }

        public Task<HaInstanceInfo?> GetInstanceInfoAsync(
            string webhookId, CancellationToken ct = default) =>
            Task.FromResult<HaInstanceInfo?>(new HaInstanceInfo { DeviceId = "device" });

        public Task<HaConfigInfo?> GetConfigAsync(CancellationToken ct = default) =>
            Task.FromResult<HaConfigInfo?>(null);

        public async Task UpdatesReachedAsync(int count)
        {
            using var timeout = new CancellationTokenSource(Timeout);
            while (UpdateCount < count)
                await _updates.Reader.ReadAsync(timeout.Token);
        }

        public TaskCompletionSource HoldNextUpdate()
        {
            var release = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.Null(Interlocked.Exchange(ref _updateRelease, release));
            return release;
        }

        private bool TryConsumeFailure()
        {
            while (true)
            {
                var current = Volatile.Read(ref _failUpdates);
                if (current == 0) return false;
                if (Interlocked.CompareExchange(ref _failUpdates, current - 1, current) == current)
                    return true;
            }
        }
    }

    private sealed class AttemptFactory(Func<int, IHaSocket> create)
    {
        private readonly Channel<int> _attempts = Channel.CreateUnbounded<int>();
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public IHaSocket Create()
        {
            var count = Interlocked.Increment(ref _count);
            _attempts.Writer.TryWrite(count);
            return create(count);
        }

        public async Task ReachedAsync(int count)
        {
            using var timeout = new CancellationTokenSource(Timeout);
            while (Count < count)
                await _attempts.Reader.ReadAsync(timeout.Token);
        }
    }

    private sealed class StubSource : ISensorSource
    {
        public IReadOnlyList<SensorDefinition> Definitions { get; } =
        [
            new("stub", "Stub", "Test sensor.", SensorPrivacy.Benign, true)
        ];

        public IReadOnlyList<Sensor> Read(
            IReadOnlySet<string> enabled, SensorReadContext context) =>
        [
            new() { UniqueId = "stub", Name = "Stub", State = 1 }
        ];

        public void Start(Action onChanged) { }
        public void Stop() { }
    }

    private sealed class ScriptedSocket : IHaSocket
    {
        private readonly Channel<string?> _messages = Channel.CreateUnbounded<string?>();
        public int ConnectCount { get; private set; }

        public ScriptedSocket(params string[] messages)
        {
            foreach (var message in messages) _messages.Writer.TryWrite(message);
        }

        public Task ConnectAsync(Uri uri, CancellationToken ct)
        {
            ConnectCount++;
            return Task.CompletedTask;
        }

        public Task SendAsync(string json, CancellationToken ct) => Task.CompletedTask;

        public async Task<string?> ReceiveAsync(CancellationToken ct) =>
            await _messages.Reader.ReadAsync(ct);

        public void Dispose() { }
    }

    private sealed class ThrowingSocket : IHaSocket
    {
        public Task ConnectAsync(Uri uri, CancellationToken ct) =>
            throw new IOException("offline");
        public Task SendAsync(string json, CancellationToken ct) => Task.CompletedTask;
        public Task<string?> ReceiveAsync(CancellationToken ct) => Task.FromResult<string?>(null);
        public void Dispose() { }
    }

    private sealed class ClosingSocket : IHaSocket
    {
        public Task ConnectAsync(Uri uri, CancellationToken ct) => Task.CompletedTask;
        public Task SendAsync(string json, CancellationToken ct) => Task.CompletedTask;
        public Task<string?> ReceiveAsync(CancellationToken ct) => Task.FromResult<string?>(null);
        public void Dispose() { }
    }

    private sealed class BlockingSocket : IHaSocket
    {
        public Task ConnectAsync(Uri uri, CancellationToken ct) => Task.CompletedTask;
        public Task SendAsync(string json, CancellationToken ct) => Task.CompletedTask;

        public async Task<string?> ReceiveAsync(CancellationToken ct)
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
            return null;
        }

        public void Dispose() { }
    }

    private sealed class BlockingConnectSocket : IHaSocket
    {
        private int _active;
        private int _maximum;
        private int _connectCount;

        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int MaximumConcurrentConnects => Volatile.Read(ref _maximum);
        public int ConnectCount => Volatile.Read(ref _connectCount);

        public async Task ConnectAsync(Uri uri, CancellationToken ct)
        {
            Interlocked.Increment(ref _connectCount);
            var active = Interlocked.Increment(ref _active);
            UpdateMaximum(active);
            Entered.TrySetResult();
            try
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public Task SendAsync(string json, CancellationToken ct) => Task.CompletedTask;
        public Task<string?> ReceiveAsync(CancellationToken ct) => Task.FromResult<string?>(null);
        public void Dispose() { }

        private void UpdateMaximum(int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maximum);
                if (current >= value) return;
                if (Interlocked.CompareExchange(ref _maximum, value, current) == current) return;
            }
        }
    }

    private sealed class ControlledFailSocket : IHaSocket
    {
        private readonly TaskCompletionSource _failure =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ConnectAsync(Uri uri, CancellationToken ct)
        {
            Entered.TrySetResult();
            await _failure.Task.WaitAsync(ct);
            throw new IOException("offline");
        }

        public void Fail() => _failure.TrySetResult();
        public Task SendAsync(string json, CancellationToken ct) => Task.CompletedTask;
        public Task<string?> ReceiveAsync(CancellationToken ct) => Task.FromResult<string?>(null);
        public void Dispose() { }
    }

    private sealed class AuthenticatedSocket : IHaSocket
    {
        private readonly Channel<object?> _messages = Channel.CreateUnbounded<object?>();

        public AuthenticatedSocket()
        {
            _messages.Writer.TryWrite("""{"type":"auth_required"}""");
            _messages.Writer.TryWrite("""{"type":"auth_ok"}""");
        }

        public TaskCompletionSource Authenticated { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ConnectAsync(Uri uri, CancellationToken ct) => Task.CompletedTask;

        public Task SendAsync(string json, CancellationToken ct)
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("type", out var type)
                && type.GetString() == "mobile_app/push_notification_channel")
            {
                Authenticated.TrySetResult();
            }

            return Task.CompletedTask;
        }

        public async Task<string?> ReceiveAsync(CancellationToken ct)
        {
            var message = await _messages.Reader.ReadAsync(ct);
            if (message is Exception failure) throw failure;
            return message as string;
        }

        public void Fail() => _messages.Writer.TryWrite(new IOException("connection lost"));

        public void Dispose() { }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        private readonly ConcurrentQueue<LogLevel> _levels = new();

        public int Count(LogLevel level) => _levels.Count(entry => entry == level);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _levels.Enqueue(logLevel);
    }
}
