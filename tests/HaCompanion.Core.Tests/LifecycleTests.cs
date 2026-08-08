using HaCompanion.Core.Abstractions;
using HaCompanion.Core.Lifecycle;
using HaCompanion.Core.Sensors;

namespace HaCompanion.Core.Tests;

public class LifecycleMessageTests
{
    [Theory]
    [InlineData(WindowsLifecycleMessages.PBT_APMSUSPEND, LifecycleTransition.Sleeping)]
    [InlineData(WindowsLifecycleMessages.PBT_APMRESUMESUSPEND, LifecycleTransition.Running)]
    [InlineData(WindowsLifecycleMessages.PBT_APMRESUMEAUTOMATIC, LifecycleTransition.Running)]
    [InlineData(WindowsLifecycleMessages.PBT_APMRESUMECRITICAL, LifecycleTransition.Running)]
    public void Power_broadcast_maps_suspend_and_every_resume(int eventType, LifecycleTransition expected)
    {
        var signal = WindowsLifecycleMessages.MapWindowMessage(
            WindowsLifecycleMessages.WM_POWERBROADCAST, eventType, 0);

        Assert.NotNull(signal);
        Assert.Equal(expected, signal!.Value.Transition);
    }

    [Fact]
    public void Critical_resume_is_flagged_so_the_reason_survives()
    {
        var signal = WindowsLifecycleMessages.MapPowerBroadcast(
            WindowsLifecycleMessages.PBT_APMRESUMECRITICAL);

        Assert.NotNull(signal);
        Assert.True(signal!.Value.Critical);
    }

    [Fact]
    public void Power_setting_changes_are_not_lifecycle_events()
    {
        // Display and lid notifications arrive on the same message; reporting them
        // would flap the state for something that is not a transition at all.
        Assert.Null(WindowsLifecycleMessages.MapPowerBroadcast(
            WindowsLifecycleMessages.PBT_POWERSETTINGCHANGE));
    }

    [Fact]
    public void Query_end_session_with_the_logoff_flag_is_a_sign_out()
    {
        var signal = WindowsLifecycleMessages.MapWindowMessage(
            WindowsLifecycleMessages.WM_QUERYENDSESSION,
            0,
            unchecked((nint)WindowsLifecycleMessages.ENDSESSION_LOGOFF));

        Assert.NotNull(signal);
        Assert.Equal(LifecycleTransition.SigningOut, signal!.Value.Transition);
    }

    [Fact]
    public void Query_end_session_without_flags_is_a_shutdown_or_restart()
    {
        // Windows never says which; the reason has to admit that.
        var signal = WindowsLifecycleMessages.MapWindowMessage(
            WindowsLifecycleMessages.WM_QUERYENDSESSION, 0, 0);

        Assert.NotNull(signal);
        Assert.Equal(LifecycleTransition.ShuttingDown, signal!.Value.Transition);
        Assert.Equal("Shutdown or restart", signal.Value.Reason);
        Assert.False(signal.Value.Critical);
    }

    [Fact]
    public void Critical_shutdown_is_distinguished()
    {
        var signal = WindowsLifecycleMessages.MapWindowMessage(
            WindowsLifecycleMessages.WM_ENDSESSION,
            1,
            WindowsLifecycleMessages.ENDSESSION_CRITICAL);

        Assert.NotNull(signal);
        Assert.Equal(LifecycleTransition.ShuttingDown, signal!.Value.Transition);
        Assert.True(signal.Value.Critical);
    }

    [Fact]
    public void End_session_with_false_means_another_app_cancelled_the_shutdown()
    {
        var signal = WindowsLifecycleMessages.MapWindowMessage(
            WindowsLifecycleMessages.WM_ENDSESSION, 0, 0);

        Assert.NotNull(signal);
        Assert.Equal(LifecycleTransition.Running, signal!.Value.Transition);
    }

    [Fact]
    public void Unrelated_window_messages_are_ignored()
    {
        Assert.Null(WindowsLifecycleMessages.MapWindowMessage(0x0005 /* WM_SIZE */, 0, 0));
    }

    [Theory]
    [InlineData(WindowsLifecycleMessages.PowerModeSuspend, LifecycleTransition.Sleeping)]
    [InlineData(WindowsLifecycleMessages.PowerModeResume, LifecycleTransition.Running)]
    public void Managed_power_mode_maps_to_the_same_transitions(int mode, LifecycleTransition expected)
    {
        var signal = WindowsLifecycleMessages.MapPowerMode(mode);

        Assert.NotNull(signal);
        Assert.Equal(expected, signal!.Value.Transition);
    }

    [Fact]
    public void Managed_power_status_change_is_ignored()
    {
        Assert.Null(WindowsLifecycleMessages.MapPowerMode(3));
    }

    [Theory]
    [InlineData(WindowsLifecycleMessages.SessionEndReasonLogoff, LifecycleTransition.SigningOut)]
    [InlineData(WindowsLifecycleMessages.SessionEndReasonSystemShutdown, LifecycleTransition.ShuttingDown)]
    public void Session_end_reasons_map(int reason, LifecycleTransition expected)
    {
        var signal = WindowsLifecycleMessages.MapSessionEndReason(reason);

        Assert.NotNull(signal);
        Assert.Equal(expected, signal!.Value.Transition);
    }

    [Fact]
    public void Unknown_session_end_reason_is_ignored()
    {
        Assert.Null(WindowsLifecycleMessages.MapSessionEndReason(99));
    }

    [Fact]
    public void Only_sign_out_is_taken_from_session_switch()
    {
        // Lock, unlock and fast user switching belong to the Active sensor. Two
        // entities disagreeing about the same fact is worse than one.
        var signOut = WindowsLifecycleMessages.MapSessionSwitch(
            WindowsLifecycleMessages.SessionSwitchLogoff);

        Assert.NotNull(signOut);
        Assert.Equal(LifecycleTransition.SigningOut, signOut!.Value.Transition);
        Assert.Null(WindowsLifecycleMessages.MapSessionSwitch(7 /* SessionLock */));
        Assert.Null(WindowsLifecycleMessages.MapSessionSwitch(2 /* ConsoleDisconnect */));
    }

    [Theory]
    [InlineData(LifecycleTransition.Running, "running")]
    [InlineData(LifecycleTransition.Sleeping, "sleeping")]
    [InlineData(LifecycleTransition.Hibernating, "hibernating")]
    [InlineData(LifecycleTransition.SigningOut, "signing_out")]
    [InlineData(LifecycleTransition.ShuttingDown, "shutting_down")]
    [InlineData(LifecycleTransition.Restarting, "restarting")]
    public void States_are_stable_enumerated_strings(LifecycleTransition transition, string expected)
    {
        Assert.Equal(expected, LifecycleStateFormatter.Describe(transition));
        Assert.StartsWith("mdi:", LifecycleStateFormatter.IconFor(transition));
    }
}

public class LifecycleTrackerTests
{
    private static readonly DateTimeOffset At = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Starts_running()
    {
        var tracker = new LifecycleTracker();

        Assert.Equal(LifecycleTransition.Running, tracker.Current);
        Assert.Null(tracker.ChangedAt);
    }

    [Fact]
    public void First_suspend_changes_state_and_asks_for_a_final_push()
    {
        var tracker = new LifecycleTracker();

        var result = tracker.Observe(new LifecycleSignal(LifecycleTransition.Sleeping, "Suspend"), At);

        Assert.True(result.Changed);
        Assert.True(result.RequiresFinalPush);
        Assert.Equal(At, tracker.ChangedAt);
    }

    [Fact]
    public void Repeated_notifications_for_the_same_transition_are_idempotent()
    {
        // A shutdown delivers WM_QUERYENDSESSION, WM_ENDSESSION and SessionEnding
        // for one event. Only the first may push.
        var tracker = new LifecycleTracker();
        var signal = new LifecycleSignal(LifecycleTransition.ShuttingDown, "Shutdown or restart");

        Assert.True(tracker.Observe(signal, At).Changed);
        Assert.False(tracker.Observe(signal, At.AddSeconds(1)).Changed);
        Assert.False(tracker.Observe(signal, At.AddSeconds(2)).RequiresFinalPush);
        Assert.Equal(At, tracker.ChangedAt);
    }

    [Fact]
    public void A_shutdown_is_not_downgraded_by_a_suspend_that_follows_it()
    {
        var tracker = new LifecycleTracker();
        tracker.Observe(new LifecycleSignal(LifecycleTransition.ShuttingDown, "Shutdown or restart"), At);

        var result = tracker.Observe(
            new LifecycleSignal(LifecycleTransition.Sleeping, "Suspend"), At.AddSeconds(1));

        Assert.False(result.Changed);
        Assert.Equal(LifecycleTransition.ShuttingDown, tracker.Current);
    }

    [Fact]
    public void A_shutdown_during_a_sign_out_wins()
    {
        var tracker = new LifecycleTracker();
        tracker.Observe(new LifecycleSignal(LifecycleTransition.SigningOut, "Sign-out"), At);

        Assert.True(tracker.Observe(
            new LifecycleSignal(LifecycleTransition.ShuttingDown, "Shutdown or restart"), At).Changed);
        Assert.Equal(LifecycleTransition.ShuttingDown, tracker.Current);
    }

    [Fact]
    public void Resume_always_wins_because_only_a_live_process_can_see_it()
    {
        var tracker = new LifecycleTracker();
        tracker.Observe(new LifecycleSignal(LifecycleTransition.ShuttingDown, "Shutdown or restart"), At);

        var result = tracker.Observe(LifecycleSignal.Running("Session end cancelled"), At.AddSeconds(5));

        Assert.True(result.Changed);
        Assert.False(result.RequiresFinalPush);
        Assert.Equal(LifecycleTransition.Running, tracker.Current);
        Assert.Equal("Session end cancelled", tracker.Reason);
    }

    [Fact]
    public void Resume_while_already_running_changes_nothing()
    {
        var tracker = new LifecycleTracker();

        Assert.False(tracker.Observe(LifecycleSignal.Running("Resume"), At).Changed);
    }
}

public class LifecycleJournalTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "ha-companion-lifecycle-" + Guid.NewGuid().ToString("N"));

    private string JournalPath => Path.Combine(_directory, "lifecycle.json");

    [Fact]
    public void Round_trips_a_record()
    {
        var journal = new FileLifecycleJournal(JournalPath);
        var record = new LifecycleRecord
        {
            Transition = LifecycleTransition.ShuttingDown,
            ObservedAt = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero),
            Reason = "Shutdown or restart",
            Critical = true
        };

        journal.Write(record);
        var loaded = journal.Read();

        Assert.Equal(record, loaded);
        Assert.False(loaded!.Acknowledged);
    }

    [Fact]
    public void Missing_file_reads_as_nothing_known()
    {
        Assert.Null(new FileLifecycleJournal(JournalPath).Read());
    }

    [Fact]
    public void A_truncated_file_is_treated_as_no_record_rather_than_throwing()
    {
        // Written while Windows was killing the process: it must cost a forgotten
        // transition, not a crash on the next start.
        Directory.CreateDirectory(_directory);
        File.WriteAllText(JournalPath, "{\"Transition\":\"Shutt");

        Assert.Null(new FileLifecycleJournal(JournalPath).Read());
    }

    [Fact]
    public void An_unwritable_path_never_throws()
    {
        // A failing journal must not be able to hang or crash app exit.
        Directory.CreateDirectory(_directory);
        var blocking = Path.Combine(_directory, "blocked");
        File.WriteAllText(blocking, string.Empty);
        var journal = new FileLifecycleJournal(Path.Combine(blocking, "lifecycle.json"));

        journal.Write(new LifecycleRecord());

        Assert.Null(journal.Read());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }
}

public class LifecycleCoordinatorTests
{
    private static readonly DateTimeOffset At = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_transition_is_journalled_before_anything_is_sent()
    {
        var journal = new FakeJournal();
        var coordinator = new LifecycleCoordinator(journal, clock: new FixedClock(At));

        coordinator.Observe(new LifecycleSignal(LifecycleTransition.ShuttingDown, "Shutdown or restart"));

        Assert.NotNull(journal.Record);
        Assert.Equal(LifecycleTransition.ShuttingDown, journal.Record!.Transition);
        Assert.Equal(At, journal.Record.ObservedAt);
        Assert.False(journal.Record.Acknowledged);
    }

    [Fact]
    public async Task The_final_push_runs_once_and_is_bounded_by_its_timeout()
    {
        var attempts = 0;
        var coordinator = new LifecycleCoordinator(
            new FakeJournal(),
            finalPush: async token =>
            {
                Interlocked.Increment(ref attempts);
                await Task.Delay(Timeout.Infinite, token);
                return true;
            },
            finalPushTimeout: TimeSpan.FromMilliseconds(50));

        coordinator.Observe(new LifecycleSignal(LifecycleTransition.Sleeping, "Suspend"));
        Assert.NotNull(coordinator.FinalPush);
        await coordinator.FinalPush!;

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task A_failing_final_push_leaves_the_transition_unacknowledged()
    {
        var journal = new FakeJournal();
        var coordinator = new LifecycleCoordinator(
            journal,
            finalPush: _ => throw new HttpRequestException("network already gone"));

        coordinator.Observe(new LifecycleSignal(LifecycleTransition.ShuttingDown, "Shutdown or restart"));
        Assert.NotNull(coordinator.FinalPush);
        await coordinator.FinalPush!;

        Assert.NotNull(journal.Record);
        Assert.False(journal.Record!.Acknowledged);
    }

    [Fact]
    public void Delivery_is_only_recorded_for_a_batch_that_actually_read_the_transition()
    {
        var journal = new FakeJournal();
        var coordinator = new LifecycleCoordinator(journal);

        // A sync that was already in flight completes: it cannot have carried this.
        coordinator.ReportDelivered();
        coordinator.Observe(new LifecycleSignal(LifecycleTransition.Sleeping, "Suspend"));
        coordinator.ReportDelivered();
        Assert.NotNull(journal.Record);
        Assert.False(journal.Record!.Acknowledged);

        coordinator.NoteRead();
        coordinator.ReportDelivered();

        Assert.True(journal.Record!.Acknowledged);
        Assert.NotNull(coordinator.Pending);
        Assert.True(coordinator.Pending!.Acknowledged);
    }

    [Fact]
    public async Task A_resume_cancels_a_suspend_push_that_is_still_waiting()
    {
        var cancelled = false;
        var started = new ManualResetEventSlim();
        var coordinator = new LifecycleCoordinator(
            new FakeJournal(),
            finalPush: async token =>
            {
                started.Set();
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    throw;
                }

                return true;
            },
            finalPushTimeout: TimeSpan.FromMinutes(5));

        coordinator.Observe(new LifecycleSignal(LifecycleTransition.Sleeping, "Suspend"));
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

        coordinator.Observe(LifecycleSignal.Running("Resume"));
        Assert.NotNull(coordinator.FinalPush);
        await coordinator.FinalPush!.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(cancelled);
    }

    [Fact]
    public void Startup_recovers_a_transition_the_previous_run_never_delivered()
    {
        var journal = new FakeJournal
        {
            Record = new LifecycleRecord
            {
                Transition = LifecycleTransition.ShuttingDown,
                ObservedAt = At,
                Reason = "Shutdown or restart"
            }
        };
        var coordinator = new LifecycleCoordinator(journal);

        coordinator.Start();

        Assert.NotNull(coordinator.Pending);
        Assert.Equal(LifecycleTransition.ShuttingDown, coordinator.Pending!.Transition);
        Assert.False(coordinator.PendingIsCurrent);
        Assert.Equal(LifecycleTransition.Running, coordinator.Tracker.Current);
    }

    [Fact]
    public void An_acknowledged_transition_is_not_recovered_again()
    {
        var journal = new FakeJournal
        {
            Record = new LifecycleRecord
            {
                Transition = LifecycleTransition.ShuttingDown,
                ObservedAt = At,
                Acknowledged = true
            }
        };
        var coordinator = new LifecycleCoordinator(journal);

        coordinator.Start();

        Assert.Null(coordinator.Pending);
    }

    [Fact]
    public void A_power_cut_leaves_nothing_to_recover()
    {
        var coordinator = new LifecycleCoordinator(new FakeJournal());

        coordinator.Start();

        Assert.Null(coordinator.Pending);
        Assert.Equal(LifecycleTransition.Running, coordinator.Tracker.Current);
    }

    [Fact]
    public void Only_real_changes_ask_for_a_push()
    {
        var pushes = 0;
        var coordinator = new LifecycleCoordinator(new FakeJournal());
        coordinator.Changed += () => pushes++;

        coordinator.Observe(new LifecycleSignal(LifecycleTransition.Sleeping, "Suspend"));
        coordinator.Observe(new LifecycleSignal(LifecycleTransition.Sleeping, "Suspend"));

        Assert.Equal(1, pushes);
    }

    [Fact]
    public async Task Simultaneous_reports_of_one_transition_push_exactly_once()
    {
        // Windows delivers the same fact on the message pump and on SystemEvents, so
        // two threads routinely report a suspend at the same moment.
        const int threads = 16;
        var changed = 0;
        var pushes = 0;
        var ready = new Barrier(threads);
        var coordinator = new LifecycleCoordinator(
            new LockingJournal(),
            finalPush: _ =>
            {
                Interlocked.Increment(ref pushes);
                return Task.FromResult(true);
            });
        coordinator.Changed += () => Interlocked.Increment(ref changed);

        await Task.WhenAll(Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            ready.SignalAndWait();
            coordinator.Observe(new LifecycleSignal(LifecycleTransition.Sleeping, "Suspend"));
        })));

        Assert.NotNull(coordinator.FinalPush);
        await coordinator.FinalPush!.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, Volatile.Read(ref changed));
        Assert.Equal(1, Volatile.Read(ref pushes));
    }

    [Fact]
    public async Task A_resume_racing_the_suspend_still_cancels_its_push()
    {
        // The cancellation source is published before the observation completes, so a
        // resume on another thread can never slip past the push it should cancel.
        for (var attempt = 0; attempt < 25; attempt++)
        {
            CancellationToken token = default;
            var entered = new ManualResetEventSlim();
            var coordinator = new LifecycleCoordinator(
                new LockingJournal(),
                finalPush: async ct =>
                {
                    token = ct;
                    entered.Set();
                    await Task.Delay(Timeout.Infinite, ct);
                    return true;
                },
                finalPushTimeout: TimeSpan.FromMinutes(5));

            var suspend = Task.Run(() =>
                coordinator.Observe(new LifecycleSignal(LifecycleTransition.Sleeping, "Suspend")));
            var resume = Task.Run(() =>
            {
                entered.Wait(TimeSpan.FromSeconds(5));
                coordinator.Observe(LifecycleSignal.Running("Resume"));
            });

            await Task.WhenAll(suspend, resume).WaitAsync(TimeSpan.FromSeconds(10));
            await coordinator.FinalPush!.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(token.IsCancellationRequested);
            Assert.Equal(LifecycleTransition.Running, coordinator.Tracker.Current);
        }
    }

    [Fact]
    public async Task A_more_serious_transition_replaces_the_push_of_the_previous_one()
    {
        var tokens = new List<CancellationToken>();
        var entered = new SemaphoreSlim(0);
        var coordinator = new LifecycleCoordinator(
            new LockingJournal(),
            finalPush: async ct =>
            {
                lock (tokens) tokens.Add(ct);
                entered.Release();
                await Task.Delay(Timeout.Infinite, ct);
                return true;
            },
            finalPushTimeout: TimeSpan.FromMinutes(5));

        coordinator.Observe(new LifecycleSignal(LifecycleTransition.Sleeping, "Suspend"));
        Assert.True(await entered.WaitAsync(TimeSpan.FromSeconds(5)));
        var first = coordinator.FinalPush;

        coordinator.Observe(new LifecycleSignal(LifecycleTransition.ShuttingDown, "Shutdown", Critical: true));
        Assert.True(await entered.WaitAsync(TimeSpan.FromSeconds(5)));
        var second = coordinator.FinalPush;

        Assert.NotSame(first, second);
        await first!.WaitAsync(TimeSpan.FromSeconds(5));

        CancellationToken[] observed;
        lock (tokens) observed = tokens.ToArray();

        Assert.Equal(2, observed.Length);
        Assert.True(observed[0].IsCancellationRequested);
        Assert.False(observed[1].IsCancellationRequested);

        coordinator.Stop();
        await second!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(observed[1].IsCancellationRequested);
    }

    [Fact]
    public async Task Stopping_after_the_push_finished_is_harmless()
    {
        // The push retires its own cancellation source under the same lock Stop uses,
        // so a late stop can never touch a disposed one.
        var coordinator = new LifecycleCoordinator(
            new LockingJournal(),
            finalPush: _ => Task.FromResult(true));

        coordinator.Observe(new LifecycleSignal(LifecycleTransition.ShuttingDown, "Shutdown"));
        await coordinator.FinalPush!.WaitAsync(TimeSpan.FromSeconds(5));

        coordinator.Stop();
        coordinator.Stop();
    }

    [Fact]
    public async Task Stopping_while_observations_arrive_never_throws()
    {
        var coordinator = new LifecycleCoordinator(
            new LockingJournal(),
            finalPush: async ct =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), ct);
                return true;
            },
            finalPushTimeout: TimeSpan.FromSeconds(30));

        var signals = Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                coordinator.Observe(new LifecycleSignal(LifecycleTransition.Sleeping, "Suspend"));
                coordinator.Observe(LifecycleSignal.Running("Resume"));
            }
        });
        var stops = Task.Run(() =>
        {
            for (var i = 0; i < 200; i++) coordinator.Stop();
        });

        await Task.WhenAll(signals, stops).WaitAsync(TimeSpan.FromSeconds(30));

        var push = coordinator.FinalPush;
        if (push is not null)
        {
            try
            {
                await push.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException)
            {
                // Expected: the coordinator swallows cancellation itself.
            }
        }
    }

    private sealed class FakeJournal : ILifecycleJournal
    {
        public LifecycleRecord? Record { get; set; }

        public LifecycleRecord? Read() => Record;

        public void Write(LifecycleRecord record) => Record = record;
    }

    /// <summary>A journal that tolerates the concurrent writes the race tests produce.</summary>
    private sealed class LockingJournal : ILifecycleJournal
    {
        private readonly object _gate = new();
        private LifecycleRecord? _record;

        public LifecycleRecord? Read()
        {
            lock (_gate) return _record;
        }

        public void Write(LifecycleRecord record)
        {
            lock (_gate) _record = record;
        }
    }
}

public class LifecycleSensorSourceTests
{
    private static readonly DateTimeOffset At = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlySet<string> All =
        new HashSet<string> { LifecycleSensorSource.SystemStateId };

    [Fact]
    public void Reports_running_with_no_stale_transition_attached()
    {
        var (source, _, _) = Build();

        var sensor = Assert.Single(source.Read(All, SensorReadContext.Periodic));

        Assert.Equal("system_state", sensor.UniqueId);
        Assert.Equal("running", sensor.State);
        Assert.Equal("diagnostic", sensor.EntityCategory);
        Assert.DoesNotContain("Last Unreported Transition", sensor.Attributes!.Keys);
    }

    [Fact]
    public void Reports_the_current_transition_without_repeating_it_as_unreported()
    {
        var (source, signals, _) = Build();
        source.Start(() => { });

        signals.Raise(new LifecycleSignal(LifecycleTransition.ShuttingDown, "Shutdown or restart"));
        var sensor = Assert.Single(source.Read(All, SensorReadContext.LifecycleTransition));

        Assert.Equal("shutting_down", sensor.State);
        Assert.Equal("Shutdown or restart", sensor.Attributes!["Reason"]);
        Assert.Equal(At.ToString("o"), sensor.Attributes["Since"]);
        Assert.DoesNotContain("Last Unreported Transition", sensor.Attributes.Keys);
    }

    [Fact]
    public void After_a_resume_the_undelivered_suspend_is_reported_as_an_attribute()
    {
        // The state has to be "running" - the machine is - so the transition Home
        // Assistant never heard about travels alongside it instead.
        var (source, signals, _) = Build();
        source.Start(() => { });

        signals.Raise(new LifecycleSignal(LifecycleTransition.Sleeping, "Suspend"));
        signals.Raise(LifecycleSignal.Running("Resume"));
        var sensor = Assert.Single(source.Read(All, SensorReadContext.StateChange));

        Assert.Equal("running", sensor.State);
        Assert.Equal("sleeping", sensor.Attributes!["Last Unreported Transition"]);
        Assert.Equal(At.ToString("o"), sensor.Attributes["Last Unreported At"]);
        Assert.Equal("Suspend", sensor.Attributes["Last Unreported Reason"]);
    }

    [Fact]
    public void A_recovered_transition_stops_being_reported_once_acknowledged()
    {
        var (source, signals, coordinator) = Build();
        source.Start(() => { });
        signals.Raise(new LifecycleSignal(LifecycleTransition.Sleeping, "Suspend"));
        signals.Raise(LifecycleSignal.Running("Resume"));

        source.Read(All, SensorReadContext.StateChange);
        coordinator.ReportDelivered();
        var sensor = Assert.Single(source.Read(All, SensorReadContext.Periodic));

        Assert.DoesNotContain("Last Unreported Transition", sensor.Attributes!.Keys);
    }

    [Fact]
    public void Produces_nothing_when_the_sensor_is_switched_off()
    {
        var (source, _, _) = Build();

        Assert.Empty(source.Read(new HashSet<string>(), SensorReadContext.Periodic));
    }

    [Fact]
    public void Stopping_releases_the_hook_and_restarting_reattaches_it()
    {
        var (source, signals, _) = Build();
        var pushes = 0;

        source.Start(() => pushes++);
        source.Stop();
        signals.Raise(new LifecycleSignal(LifecycleTransition.Sleeping, "Suspend"));
        Assert.Equal(0, pushes);
        Assert.True(signals.Stopped);

        source.Start(() => pushes++);
        signals.Raise(new LifecycleSignal(LifecycleTransition.Sleeping, "Suspend"));
        Assert.Equal(1, pushes);
    }

    private static (LifecycleSensorSource Source, FakeSignals Signals, LifecycleCoordinator Coordinator) Build()
    {
        var signals = new FakeSignals();
        var coordinator = new LifecycleCoordinator(new FakeJournal(), clock: new FixedClock(At));
        return (new LifecycleSensorSource(coordinator, signals), signals, coordinator);
    }

    private sealed class FakeSignals : ILifecycleSignalSource
    {
        public event Action<LifecycleSignal>? SignalObserved;

        public bool Stopped { get; private set; }

        public void Raise(LifecycleSignal signal) => SignalObserved?.Invoke(signal);

        public void Start() => Stopped = false;

        public void Stop() => Stopped = true;
    }

    private sealed class FakeJournal : ILifecycleJournal
    {
        private LifecycleRecord? _record;

        public LifecycleRecord? Read() => _record;

        public void Write(LifecycleRecord record) => _record = record;
    }
}

public class MessagePumpLifetimeTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public void A_second_start_is_refused_while_a_pump_is_running()
    {
        using var lifetime = new MessagePumpLifetime();

        Assert.True(lifetime.TryBeginStart());
        Assert.False(lifetime.TryBeginStart());

        lifetime.MarkStopped();
        Assert.True(lifetime.TryBeginStart());
    }

    [Fact]
    public void Stopping_something_that_never_started_does_nothing()
    {
        using var lifetime = new MessagePumpLifetime();

        Assert.False(lifetime.RequestStop());
        Assert.False(lifetime.StopRequested);
    }

    [Fact]
    public void A_stop_that_beats_window_creation_is_still_seen_by_the_pump()
    {
        // The bug this protects against: the stopper found no window, skipped the
        // close message, and the pump then created its window and looped forever.
        using var lifetime = new MessagePumpLifetime();
        Assert.True(lifetime.TryBeginStart());
        Assert.True(lifetime.RequestStop());

        var createdWindow = true;
        var pump = new Thread(() =>
        {
            try
            {
                if (lifetime.StopRequested)
                {
                    createdWindow = false;
                    return;
                }

                lifetime.MarkReady();
            }
            finally
            {
                lifetime.MarkStopped();
            }
        });
        pump.Start();

        Assert.True(lifetime.WaitUntilReady(Wait));
        Assert.True(pump.Join(Wait));
        Assert.False(createdWindow);
        Assert.False(lifetime.IsRunning);
    }

    [Fact]
    public void A_stopper_is_never_left_waiting_for_a_pump_that_never_ran()
    {
        using var lifetime = new MessagePumpLifetime();
        Assert.True(lifetime.TryBeginStart());
        Assert.True(lifetime.RequestStop());

        // Nobody ever announces readiness, exactly as when the thread failed to
        // start; the stopper reclaims the lifetime itself.
        lifetime.MarkStopped();

        Assert.True(lifetime.WaitUntilReady(Wait));
        Assert.True(lifetime.TryBeginStart());
    }

    [Fact]
    public void Readiness_is_published_before_a_stopper_can_act_on_it()
    {
        // Whichever order the two threads run in, the stopper either sees the handle
        // or the pump sees the request - never neither.
        for (var attempt = 0; attempt < 50; attempt++)
        {
            using var lifetime = new MessagePumpLifetime();
            Assert.True(lifetime.TryBeginStart());

            var handle = 0;
            var start = new Barrier(2);
            var pump = new Thread(() =>
            {
                try
                {
                    start.SignalAndWait();
                    if (lifetime.StopRequested) return;

                    Volatile.Write(ref handle, 42);
                    lifetime.MarkReady();
                }
                finally
                {
                    lifetime.MarkStopped();
                }
            });
            pump.Start();

            start.SignalAndWait();
            lifetime.RequestStop();
            Assert.True(lifetime.WaitUntilReady(Wait));
            Assert.True(pump.Join(Wait));

            var observed = Volatile.Read(ref handle);
            Assert.True(observed is 0 or 42);
            Assert.False(lifetime.IsRunning);
        }
    }

    [Fact]
    public void Marking_stopped_twice_leaves_the_lifetime_reusable()
    {
        using var lifetime = new MessagePumpLifetime();
        Assert.True(lifetime.TryBeginStart());

        lifetime.MarkStopped();
        lifetime.MarkStopped();

        Assert.False(lifetime.IsRunning);
        Assert.False(lifetime.StopRequested);
        Assert.True(lifetime.TryBeginStart());
    }

    [Fact]
    public void Waiting_on_a_disposed_lifetime_gives_up_instead_of_hanging()
    {
        var lifetime = new MessagePumpLifetime();
        lifetime.TryBeginStart();
        lifetime.Dispose();

        Assert.False(lifetime.WaitUntilReady(TimeSpan.FromMilliseconds(50)));
        lifetime.Dispose();
    }
}

internal sealed class FixedClock : IClock
{
    public FixedClock(DateTimeOffset now) => UtcNow = now;

    public DateTimeOffset UtcNow { get; set; }
}
