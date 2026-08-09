using System.Collections.Concurrent;
using HaCompanion.Core.Abstractions;
using HaCompanion.Core.Models;
using HaCompanion.Core.Sensors;

namespace HaCompanion.Core.Tests;

/// <summary>
/// Regression tests for how sensors behave over time rather than what they
/// report: a sensor switched off must cost nothing, a source stopped must leave
/// nothing running, and a value that has not moved must not produce traffic.
/// Everything here is driven by handshakes and invariants rather than by
/// sleeping and hoping, so a slow machine makes these tests slower, not flaky.
/// </summary>
public class SensorLifecycleTests
{
    private static readonly TimeSpan Never = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // ---------------------------------------------------------------- poll loop

    [Fact]
    public async Task Starting_polls_immediately()
    {
        var tick = new TickRecorder();
        var loop = new SensorPollLoop(tick.RunAsync, Never);

        loop.Start();

        await tick.Reached(1);
        Assert.Equal(SensorPollReason.Scheduled, tick.Reasons[0]);
        Assert.True(loop.IsRunning);

        loop.Stop();
    }

    [Fact]
    public async Task Starting_repeatedly_creates_only_one_poller()
    {
        var tick = new TickRecorder();
        var loop = new SensorPollLoop(tick.RunAsync, Never);

        for (var i = 0; i < 10; i++) loop.Start();

        await tick.Reached(1);
        await Task.Delay(100);

        // The interval is far longer than this test, so any tick beyond the
        // first would mean a second poller was created.
        Assert.Equal(1, tick.Count);

        loop.Stop();
    }

    [Fact]
    public async Task Stopping_cancels_the_collection_in_flight()
    {
        var tick = new TickRecorder { BlockUntilCancelled = true };
        var loop = new SensorPollLoop(tick.RunAsync, Never);

        loop.Start();
        await tick.Entered.Task.WaitAsync(Timeout);
        loop.Stop();

        await tick.Cancelled.Task.WaitAsync(Timeout);
        Assert.False(loop.IsRunning);
    }

    [Fact]
    public async Task Stopping_prevents_any_further_collection()
    {
        var tick = new TickRecorder();
        var loop = new SensorPollLoop(tick.RunAsync, TimeSpan.FromMilliseconds(5));

        loop.Start();
        await tick.Reached(3);
        loop.Stop();

        // Let any loop that survived the stop run several more intervals.
        await Task.Delay(150);
        var afterStop = tick.Count;
        await Task.Delay(150);

        Assert.Equal(afterStop, tick.Count);
    }

    [Fact]
    public async Task Stopping_then_starting_resumes_polling()
    {
        var tick = new TickRecorder();
        var loop = new SensorPollLoop(tick.RunAsync, Never);

        loop.Start();
        await tick.Reached(1);
        loop.Stop();
        Assert.False(loop.IsRunning);

        loop.Start();
        await tick.Reached(2);
        Assert.True(loop.IsRunning);

        loop.Stop();
    }

    [Fact]
    public async Task Repeated_start_stop_cycles_leave_nothing_running()
    {
        var tick = new TickRecorder();
        var loop = new SensorPollLoop(tick.RunAsync, TimeSpan.FromMilliseconds(1));

        for (var i = 0; i < 200; i++)
        {
            loop.Start();
            loop.Stop();
        }

        Assert.False(loop.IsRunning);

        // A cancellation source disposed by one cycle and reused by the next
        // would surface here rather than as a crash in the tray app.
        Assert.Null(tick.Failure);

        await Task.Delay(100);
        var settled = tick.Count;
        await Task.Delay(100);
        Assert.Equal(settled, tick.Count);
    }

    [Fact]
    public async Task Refreshes_never_overlap_the_poller()
    {
        var tick = new TickRecorder { HoldFor = TimeSpan.FromMilliseconds(2) };
        var loop = new SensorPollLoop(tick.RunAsync, TimeSpan.FromMilliseconds(1));

        loop.Start();
        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => loop.RunOnceAsync()));
        loop.Stop();

        Assert.Equal(1, tick.MaxConcurrency);
        Assert.Null(tick.Failure);
    }

    [Fact]
    public async Task Refreshing_without_a_running_loop_still_collects_once()
    {
        var tick = new TickRecorder();
        var loop = new SensorPollLoop(tick.RunAsync, Never);

        await loop.RunOnceAsync();

        Assert.Equal(1, tick.Count);
        Assert.Equal(SensorPollReason.Requested, tick.Reasons[0]);
        Assert.False(loop.IsRunning);
    }

    [Fact]
    public async Task Refreshing_after_a_stop_still_collects()
    {
        var tick = new TickRecorder();
        var loop = new SensorPollLoop(tick.RunAsync, Never);

        loop.Start();
        await tick.Reached(1);
        loop.Stop();

        await loop.RunOnceAsync();

        Assert.Equal(2, tick.Count);
        Assert.Equal(SensorPollReason.Requested, tick.Reasons[1]);
    }

    [Fact]
    public async Task Stopping_during_a_refresh_is_not_an_error()
    {
        var tick = new TickRecorder { BlockUntilCancelled = true };
        var loop = new SensorPollLoop(tick.RunAsync, Never);

        loop.Start();
        await tick.Entered.Task.WaitAsync(Timeout);

        var refresh = loop.RunOnceAsync();
        loop.Stop();

        // The user disabled the sensor mid-refresh: the refresh gives up quietly
        // instead of surfacing a cancellation the caller never asked for.
        await refresh.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Refresh_honours_the_callers_cancellation()
    {
        var tick = new TickRecorder { BlockUntilCancelled = true };
        var loop = new SensorPollLoop(tick.RunAsync, Never);
        using var caller = new CancellationTokenSource();

        var refresh = loop.RunOnceAsync(caller.Token);
        await tick.Entered.Task.WaitAsync(Timeout);
        caller.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
    }

    [Fact]
    public async Task A_failing_scheduled_collection_does_not_kill_the_poller()
    {
        var tick = new TickRecorder { FailFirstAttempts = 1 };
        var loop = new SensorPollLoop(tick.RunAsync, TimeSpan.FromMilliseconds(5));

        loop.Start();

        // A drive that is briefly unreadable must not silently retire the sensor
        // until the app is restarted.
        await tick.Reached(3);
        loop.Stop();
    }

    [Fact]
    public async Task A_failing_refresh_is_reported_to_the_caller()
    {
        var tick = new TickRecorder { FailFirstAttempts = 1 };
        var loop = new SensorPollLoop(tick.RunAsync, Never);

        await Assert.ThrowsAsync<InvalidOperationException>(() => loop.RunOnceAsync());

        // The single-flight gate is released even when the collection throws.
        await loop.RunOnceAsync();
        Assert.Equal(1, tick.Count);
    }

    [Fact]
    public void A_non_positive_interval_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SensorPollLoop((_, _) => Task.CompletedTask, TimeSpan.Zero));
    }

    // --------------------------------------------------------------- change gate

    [Fact]
    public void An_unchanged_reading_is_not_published()
    {
        var gate = new ChangeGate<string>("idle");

        Assert.False(gate.TryUpdate("idle"));
        Assert.True(gate.TryUpdate("busy"));
        Assert.False(gate.TryUpdate("busy"));
        Assert.Equal("busy", gate.Current);
    }

    [Fact]
    public void Seeding_records_a_baseline_without_publishing()
    {
        var gate = new ChangeGate<string>("unknown");

        gate.Seed("nl-NL");

        Assert.Equal("nl-NL", gate.Current);
        Assert.False(gate.TryUpdate("nl-NL"));
    }

    [Fact]
    public void Disk_drift_below_the_threshold_produces_no_traffic()
    {
        var gate = new ChangeGate<DiskUsage>(
            DiskUsage.Unavailable, DiskUsageFormatter.HasMeaningfullyChanged);
        const long total = 512L * 1024 * 1024 * 1024;

        Assert.True(gate.TryUpdate(new DiskUsage(total, 200L * 1024 * 1024 * 1024)));

        var published = gate.Current;
        var pushes = 0;
        for (var i = 1; i <= 12; i++)
        {
            // A few dozen megabytes of churn per ten-minute poll, all day long.
            var free = 200L * 1024 * 1024 * 1024 - (i * 32L * 1024 * 1024);
            if (gate.TryUpdate(new DiskUsage(total, free))) pushes++;
        }

        Assert.Equal(0, pushes);
        Assert.Equal(published, gate.Current);

        Assert.True(gate.TryUpdate(new DiskUsage(total, 150L * 1024 * 1024 * 1024)));
    }

    [Fact]
    public async Task Concurrent_writers_publish_a_change_exactly_once()
    {
        var gate = new ChangeGate<int>(0);
        var published = 0;

        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            if (gate.TryUpdate(1)) Interlocked.Increment(ref published);
        })));

        Assert.Equal(1, published);
        Assert.Equal(1, gate.Current);
    }

    // ------------------------------------------------------------------ catalog

    [Fact]
    public async Task A_disabled_source_is_never_started_read_or_refreshed()
    {
        var source = new RecordingSource();
        var catalog = new SensorCatalog([source], new SensorPreferences());

        catalog.Start(() => { });
        _ = catalog.Read(SensorReadContext.Periodic);
        await catalog.RefreshAsync();

        Assert.Equal(0, source.StartCount);
        Assert.Equal(0, source.ReadCount);
        Assert.Equal(0, source.RefreshCount);
    }

    [Fact]
    public void Enabling_a_second_sensor_does_not_restart_a_running_source()
    {
        var source = new RecordingSource();
        var catalog = new SensorCatalog([source], new SensorPreferences());
        catalog.Start(() => { });

        catalog.SetEnabled(RecordingSource.First, true);
        catalog.SetEnabled(RecordingSource.Second, true);
        catalog.SetEnabled(RecordingSource.Second, true);
        catalog.SetEnabled(RecordingSource.Third, true);

        Assert.Equal(1, source.StartCount);
        Assert.Equal(0, source.StopCount);
    }

    [Fact]
    public async Task Grouped_sensors_share_a_single_collection_per_refresh()
    {
        var source = new RecordingSource();
        var catalog = new SensorCatalog([source], new SensorPreferences());
        catalog.Start(() => { });

        catalog.SetEnabled(RecordingSource.First, true);
        catalog.SetEnabled(RecordingSource.Second, true);
        catalog.SetEnabled(RecordingSource.Third, true);

        await catalog.RefreshAsync();
        var readings = catalog.Read(SensorReadContext.Periodic);

        // Three sensors, one query: enabling the whole group must not triple the
        // cost of a sync.
        Assert.Equal(1, source.RefreshCount);
        Assert.Equal(1, source.ReadCount);
        Assert.Equal(3, readings.Count);
    }

    [Fact]
    public void A_source_stops_only_when_its_last_sensor_is_disabled()
    {
        var source = new RecordingSource();
        var catalog = new SensorCatalog([source], new SensorPreferences());
        catalog.Start(() => { });

        catalog.SetEnabled(RecordingSource.First, true);
        catalog.SetEnabled(RecordingSource.Second, true);
        catalog.SetEnabled(RecordingSource.First, false);
        Assert.Equal(0, source.StopCount);

        catalog.SetEnabled(RecordingSource.Second, false);
        Assert.Equal(1, source.StopCount);
        Assert.Equal(0, source.ReadCount);
    }

    [Fact]
    public void A_late_callback_after_stop_is_not_forwarded()
    {
        var source = new RecordingSource();
        var catalog = new SensorCatalog([source], new SensorPreferences());
        var pushes = 0;

        catalog.Start(() => pushes++);
        catalog.SetEnabled(RecordingSource.First, true);
        source.Notify();
        Assert.Equal(1, pushes);

        catalog.Stop();

        // An OS hook or timer can deliver one more callback while it is being
        // released; acting on it would push over a connection being torn down.
        source.Notify();
        Assert.Equal(1, pushes);
    }

    [Fact]
    public void A_late_callback_from_a_disabled_source_is_not_forwarded()
    {
        var source = new RecordingSource();
        var catalog = new SensorCatalog([source], new SensorPreferences());
        var pushes = 0;

        catalog.Start(() => pushes++);
        catalog.SetEnabled(RecordingSource.First, true);
        catalog.SetEnabled(RecordingSource.First, false);

        source.Notify();

        Assert.Equal(0, pushes);
    }

    [Fact]
    public void Repeated_catalog_restarts_balance_start_and_stop()
    {
        var source = new RecordingSource();
        var preferences = new SensorPreferences();
        preferences.Set(RecordingSource.First, true);
        var catalog = new SensorCatalog([source], preferences);
        var pushes = 0;

        for (var i = 0; i < 50; i++)
        {
            catalog.Start(() => pushes++);
            catalog.Stop();
        }

        Assert.Equal(50, source.StartCount);
        Assert.Equal(50, source.StopCount);

        source.Notify();
        Assert.Equal(0, pushes);
    }

    [Fact]
    public async Task Repeated_reads_do_not_start_a_stopped_source()
    {
        var source = new RecordingSource();
        var preferences = new SensorPreferences();
        preferences.Set(RecordingSource.First, true);
        var catalog = new SensorCatalog([source], preferences);

        catalog.Start(() => { });
        catalog.Stop();

        for (var i = 0; i < 5; i++)
        {
            _ = catalog.Read(SensorReadContext.Periodic);
            await catalog.RefreshAsync();
        }

        Assert.Equal(1, source.StartCount);
        Assert.Equal(1, source.StopCount);
    }

    // ------------------------------------------------------------------- winget

    [Fact]
    public async Task Scheduled_checks_with_an_unchanged_result_push_once()
    {
        var provider = new CountingWinGetProvider();
        var preferences = new SensorPreferences();
        preferences.Set(WinGetUpdateSensorSource.WinGetUpdatesId, true);
        var source = new WinGetUpdateSensorSource(
            provider, preferences, TimeSpan.FromMilliseconds(5));
        var pushes = 0;

        source.Start(() => Interlocked.Increment(ref pushes));
        await provider.Reached(4).WaitAsync(Timeout);
        source.Stop();

        // Four checks, one result: only the first is news.
        Assert.Equal(1, pushes);
    }

    [Fact]
    public async Task A_changed_update_count_pushes_again()
    {
        var provider = new CountingWinGetProvider();
        var preferences = new SensorPreferences();
        preferences.Set(WinGetUpdateSensorSource.WinGetUpdatesId, true);
        var source = new WinGetUpdateSensorSource(
            provider, preferences, TimeSpan.FromMilliseconds(5));
        var pushes = 0;

        source.Start(() => Interlocked.Increment(ref pushes));
        await provider.Reached(2).WaitAsync(Timeout);
        provider.Result = new WinGetUpdateResult(
            WinGetUpdateStatus.Ready, [new("Git", "Git.Git", "1", "2")]);
        await provider.Reached(6).WaitAsync(Timeout);
        source.Stop();

        Assert.Equal(2, pushes);
    }

    [Fact]
    public async Task Repeated_start_stop_cycles_leave_no_winget_poller_running()
    {
        var provider = new CountingWinGetProvider();
        var preferences = new SensorPreferences();
        preferences.Set(WinGetUpdateSensorSource.WinGetUpdatesId, true);
        var source = new WinGetUpdateSensorSource(
            provider, preferences, TimeSpan.FromMilliseconds(1));

        for (var i = 0; i < 100; i++)
        {
            source.Start(() => { });
            source.Stop();
        }

        await Task.Delay(100);
        var settled = provider.CheckCount;
        await Task.Delay(100);

        Assert.Equal(settled, provider.CheckCount);
    }

    // -------------------------------------------------------------------- fakes

    private sealed class TickRecorder
    {
        private readonly ConcurrentQueue<SensorPollReason> _reasons = new();
        private readonly List<(int Count, TaskCompletionSource Waiter)> _waiters = [];
        private readonly object _gate = new();
        private int _concurrency;
        private int _attempts;

        public bool BlockUntilCancelled { get; init; }
        public int FailFirstAttempts { get; init; }
        public TimeSpan HoldFor { get; init; }

        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Count => _reasons.Count;
        public IReadOnlyList<SensorPollReason> Reasons => _reasons.ToList();
        public int MaxConcurrency { get; private set; }
        public Exception? Failure { get; private set; }

        /// <summary>Completes once the loop has collected <paramref name="count"/> times.</summary>
        public Task Reached(int count)
        {
            lock (_gate)
            {
                if (_reasons.Count >= count) return Task.CompletedTask;
                var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((count, waiter));
                return waiter.Task.WaitAsync(Timeout);
            }
        }

        public async Task RunAsync(SensorPollReason reason, CancellationToken cancellationToken)
        {
            var concurrency = Interlocked.Increment(ref _concurrency);
            lock (_gate) MaxConcurrency = Math.Max(MaxConcurrency, concurrency);

            try
            {
                Entered.TrySetResult();

                if (BlockUntilCancelled)
                {
                    try
                    {
                        await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        Cancelled.TrySetResult();
                        throw;
                    }
                }
                else if (HoldFor > TimeSpan.Zero)
                {
                    await Task.Delay(HoldFor, CancellationToken.None);
                }

                if (Interlocked.Increment(ref _attempts) <= FailFirstAttempts)
                    throw new InvalidOperationException("Collection failed.");

                _reasons.Enqueue(reason);
                Release();
            }
            catch (Exception ex) when (ex is not InvalidOperationException
                                           and not OperationCanceledException)
            {
                Failure ??= ex;
                throw;
            }
            finally
            {
                Interlocked.Decrement(ref _concurrency);
            }
        }

        private void Release()
        {
            List<TaskCompletionSource> ready;
            lock (_gate)
            {
                ready = _waiters.Where(w => w.Count <= _reasons.Count).Select(w => w.Waiter).ToList();
                _waiters.RemoveAll(w => w.Count <= _reasons.Count);
            }

            foreach (var waiter in ready) waiter.TrySetResult();
        }
    }

    private sealed class RecordingSource : ISensorSource, IRefreshableSensorSource
    {
        public const string First = "recording_first";
        public const string Second = "recording_second";
        public const string Third = "recording_third";

        private Action? _onChanged;

        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public int ReadCount { get; private set; }
        public int RefreshCount { get; private set; }

        public IReadOnlyList<SensorDefinition> Definitions { get; } =
        [
            new(First, "First", "First sensor.", SensorPrivacy.Benign, false),
            new(Second, "Second", "Second sensor.", SensorPrivacy.Benign, false),
            new(Third, "Third", "Third sensor.", SensorPrivacy.Benign, false)
        ];

        public IReadOnlyList<Sensor> Read(IReadOnlySet<string> enabled, SensorReadContext context)
        {
            ReadCount++;
            return Definitions
                .Where(definition => enabled.Contains(definition.UniqueId))
                .Select(definition => new Sensor
                {
                    UniqueId = definition.UniqueId,
                    Name = definition.Name,
                    State = "on"
                })
                .ToList();
        }

        public ValueTask<IReadOnlyList<Sensor>> PreviewAsync(
            IReadOnlySet<string> requested, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Sensor>>([]);

        public void Start(Action onChanged)
        {
            StartCount++;
            _onChanged = onChanged;
        }

        public void Stop() => StopCount++;

        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            return Task.CompletedTask;
        }

        /// <summary>Replays the callback the source was handed, as a hook would.</summary>
        public void Notify() => _onChanged?.Invoke();
    }

    private sealed class CountingWinGetProvider : IWinGetUpdateProvider
    {
        private readonly object _gate = new();
        private readonly List<(int Count, TaskCompletionSource Waiter)> _waiters = [];
        private int _checkCount;

        public int CheckCount { get { lock (_gate) return _checkCount; } }

        public WinGetUpdateResult Result { get; set; } = new(WinGetUpdateStatus.Ready, []);

        public Task Reached(int count)
        {
            lock (_gate)
            {
                if (_checkCount >= count) return Task.CompletedTask;
                var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((count, waiter));
                return waiter.Task;
            }
        }

        public Task<bool> IsModuleInstalledAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<WinGetUpdateResult> CheckForUpdatesAsync(
            CancellationToken cancellationToken = default)
        {
            List<TaskCompletionSource> ready;
            lock (_gate)
            {
                _checkCount++;
                ready = _waiters.Where(w => w.Count <= _checkCount).Select(w => w.Waiter).ToList();
                _waiters.RemoveAll(w => w.Count <= _checkCount);
            }

            foreach (var waiter in ready) waiter.TrySetResult();
            return Task.FromResult(Result);
        }
    }
}
