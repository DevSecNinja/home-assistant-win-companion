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

    [Fact]
    public void Repeated_writes_and_reads_keep_the_last_record_and_hold_no_handle()
    {
        var journal = new FileLifecycleJournal(JournalPath);

        for (var i = 0; i < 200; i++)
        {
            var record = new LifecycleRecord
            {
                Transition = LifecycleTransition.Sleeping,
                ObservedAt = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero).AddSeconds(i),
                Reason = "Suspend"
            };

            journal.Write(record);
            Assert.Equal(record, journal.Read());
        }

        // A file the journal still had open could not be opened exclusively. This is
        // what a leaked stream would look like from the outside, and it matters here
        // because these writes happen while Windows is tearing the process down.
        using var exclusive = new FileStream(
            JournalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.True(exclusive.Length > 0);
    }

    [Fact]
    public void A_corrupted_file_stays_harmless_and_is_replaced_by_the_next_write()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(JournalPath, "this was never JSON");
        var journal = new FileLifecycleJournal(JournalPath);

        // Repeatedly, because startup recovery reads it on every launch: a read that
        // fails must not accumulate anything either.
        for (var i = 0; i < 25; i++) Assert.Null(journal.Read());

        var record = new LifecycleRecord
        {
            Transition = LifecycleTransition.ShuttingDown,
            ObservedAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero),
            Reason = "Shutdown or restart"
        };
        journal.Write(record);

        Assert.Equal(record, journal.Read());

        using var exclusive = new FileStream(
            JournalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.True(exclusive.Length > 0);
    }

    [Fact]
    public void A_transition_survives_restarts_until_it_is_acknowledged()
    {
        var first = new LifecycleCoordinator(new FileLifecycleJournal(JournalPath));
        first.Observe(new LifecycleSignal(LifecycleTransition.ShuttingDown, "Shutdown or restart"));

        // Each coordinator stands for one run of the app over the same file.
        var second = new LifecycleCoordinator(new FileLifecycleJournal(JournalPath));
        second.Start();
        Assert.NotNull(second.Pending);
        Assert.Equal(LifecycleTransition.ShuttingDown, second.Pending!.Transition);

        var third = new LifecycleCoordinator(new FileLifecycleJournal(JournalPath));
        third.Start();
        Assert.NotNull(third.Pending);

        // Only an acknowledgement retires it, and it stays retired.
        third.NoteRead();
        third.ReportDelivered();

        var fourth = new LifecycleCoordinator(new FileLifecycleJournal(JournalPath));
        fourth.Start();
        Assert.Null(fourth.Pending);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Keeps the lifecycle stress tests out of each other's way. They deliberately spawn
/// threads and queue work in bursts, and several of those at once on a two-core CI
/// agent starve unrelated timing-sensitive tests of a worker.
/// </summary>
[CollectionDefinition("Lifecycle stress", DisableParallelization = true)]
public class LifecycleStressCollection;

[Collection("Lifecycle stress")]
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
        const int threads = 8;
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
        for (var attempt = 0; attempt < 10; attempt++)
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
            for (var i = 0; i < 50; i++)
            {
                coordinator.Observe(new LifecycleSignal(LifecycleTransition.Sleeping, "Suspend"));
                coordinator.Observe(LifecycleSignal.Running("Resume"));
            }
        });
        var stops = Task.Run(() =>
        {
            for (var i = 0; i < 50; i++) coordinator.Stop();
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

    [Fact]
    public async Task A_push_that_runs_out_of_time_releases_its_cancellation_source()
    {
        CancellationToken token = default;
        var coordinator = new LifecycleCoordinator(
            new LockingJournal(),
            finalPush: async ct =>
            {
                token = ct;
                await Task.Delay(Timeout.Infinite, ct);
                return true;
            },
            finalPushTimeout: TimeSpan.FromMilliseconds(50));

        coordinator.Observe(new LifecycleSignal(LifecycleTransition.ShuttingDown, "Shutdown"));
        await coordinator.FinalPush!.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(token.IsCancellationRequested);

        // A source that was never disposed would keep its timer and its wait handle
        // alive; touching the handle of a disposed one throws, which is the only
        // deterministic way to observe the release from outside.
        Assert.Throws<ObjectDisposedException>(() => _ = token.WaitHandle);
    }

    [Fact]
    public async Task Each_attempt_is_replaced_rather_than_accumulated()
    {
        const int rounds = 20;
        var tokens = new List<CancellationToken>();
        var coordinator = new LifecycleCoordinator(
            new LockingJournal(),
            finalPush: ct =>
            {
                lock (tokens) tokens.Add(ct);
                return Task.FromResult(true);
            },
            finalPushTimeout: TimeSpan.FromSeconds(30));

        var tasks = new List<Task>();
        for (var i = 0; i < rounds; i++)
        {
            coordinator.Observe(new LifecycleSignal(LifecycleTransition.Sleeping, "Suspend"));
            var push = coordinator.FinalPush!;
            if (!tasks.Contains(push)) tasks.Add(push);
            coordinator.Observe(LifecycleSignal.Running("Resume"));
        }

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));

        // One attempt per suspend and no more: the resumes in between must neither
        // leave an attempt behind nor provoke an extra one.
        Assert.Equal(rounds, tasks.Count);

        CancellationToken[] observed;
        lock (tokens) observed = tokens.ToArray();

        Assert.Equal(rounds, observed.Length);

        // Every source is retired by the time its own attempt has finished, so at
        // most one is ever alive however long the loop runs.
        Assert.All(observed, t => Assert.Throws<ObjectDisposedException>(() => _ = t.WaitHandle));
    }

    [Fact]
    public async Task A_storm_of_identical_signals_is_journalled_and_pushed_once()
    {
        // SystemEvents and the message pump both repeat a suspend, and Windows sends
        // WM_QUERYENDSESSION once per top-level window.
        var journal = new CountingJournal();
        var pushes = 0;
        var changed = 0;
        var coordinator = new LifecycleCoordinator(
            journal,
            finalPush: _ =>
            {
                Interlocked.Increment(ref pushes);
                return Task.FromResult(true);
            });
        coordinator.Changed += () => Interlocked.Increment(ref changed);

        for (var i = 0; i < 250; i++)
            coordinator.Observe(new LifecycleSignal(LifecycleTransition.Sleeping, "Suspend"));

        await coordinator.FinalPush!.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, Volatile.Read(ref pushes));
        Assert.Equal(1, Volatile.Read(ref changed));
        Assert.Equal(1, journal.Writes);
    }

    [Fact]
    public async Task An_escalating_storm_pushes_once_per_distinct_transition()
    {
        var pushes = 0;
        var tasks = new List<Task>();
        var coordinator = new LifecycleCoordinator(
            new CountingJournal(),
            finalPush: _ =>
            {
                Interlocked.Increment(ref pushes);
                return Task.FromResult(true);
            },
            finalPushTimeout: TimeSpan.FromSeconds(30));

        // Severity only ever climbs here, so every repeat within a step is dropped.
        foreach (var transition in new[]
                 {
                     LifecycleTransition.Sleeping,
                     LifecycleTransition.SigningOut,
                     LifecycleTransition.ShuttingDown
                 })
        {
            for (var i = 0; i < 100; i++)
                coordinator.Observe(new LifecycleSignal(transition, transition.ToString()));

            var push = coordinator.FinalPush!;
            if (!tasks.Contains(push)) tasks.Add(push);
        }

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(3, tasks.Count);
        Assert.Equal(3, Volatile.Read(ref pushes));
        Assert.Equal(LifecycleTransition.ShuttingDown, coordinator.Tracker.Current);
    }

    [Fact]
    public async Task Stopping_prevents_the_attempt_from_reporting_afterwards()
    {
        var reported = 0;
        var entered = new SemaphoreSlim(0);
        var release = new SemaphoreSlim(0);
        var coordinator = new LifecycleCoordinator(
            new LockingJournal(),
            finalPush: async ct =>
            {
                entered.Release();
                await release.WaitAsync(ct);
                Interlocked.Increment(ref reported);
                return true;
            },
            finalPushTimeout: TimeSpan.FromMinutes(5));

        coordinator.Observe(new LifecycleSignal(LifecycleTransition.Sleeping, "Suspend"));
        Assert.True(await entered.WaitAsync(TimeSpan.FromSeconds(5)));

        coordinator.Stop();
        await coordinator.FinalPush!.WaitAsync(TimeSpan.FromSeconds(10));

        // Whatever the attempt was waiting for arriving late must not resurrect it.
        release.Release();
        await Task.Delay(50);

        Assert.Equal(0, Volatile.Read(ref reported));
    }

    [Fact]
    public async Task Many_start_and_stop_generations_leave_the_coordinator_usable()
    {
        const int generations = 25;
        var journal = new CountingJournal();
        var pushes = 0;
        var tasks = new List<Task>();
        var coordinator = new LifecycleCoordinator(
            journal,
            finalPush: _ =>
            {
                Interlocked.Increment(ref pushes);
                return Task.FromResult(true);
            },
            finalPushTimeout: TimeSpan.FromSeconds(30));

        for (var i = 0; i < generations; i++)
        {
            coordinator.Start();
            coordinator.Observe(new LifecycleSignal(LifecycleTransition.Sleeping, "Suspend"));
            coordinator.Observe(new LifecycleSignal(LifecycleTransition.Sleeping, "Suspend"));

            var push = coordinator.FinalPush!;
            if (!tasks.Contains(push)) tasks.Add(push);

            coordinator.Stop();

            // A stop leaves the machine believing it is suspended, so the next
            // generation opens with a resume, exactly as a wake would.
            coordinator.Observe(LifecycleSignal.Running("Resume"));
        }

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));

        // One attempt and one journal write per generation: the duplicate signals
        // and the repeated starts add nothing.
        Assert.Equal(generations, tasks.Count);
        Assert.Equal(generations, Volatile.Read(ref pushes));
        Assert.Equal(generations, journal.Writes);
        Assert.Equal(LifecycleTransition.Running, coordinator.Tracker.Current);
        Assert.False(coordinator.PendingIsCurrent);
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

    /// <summary>Counts writes, so a storm can be shown to cost one of them.</summary>
    private sealed class CountingJournal : ILifecycleJournal
    {
        private readonly object _gate = new();
        private LifecycleRecord? _record;
        private int _writes;

        public int Writes => Volatile.Read(ref _writes);

        public LifecycleRecord? Read()
        {
            lock (_gate) return _record;
        }

        public void Write(LifecycleRecord record)
        {
            lock (_gate)
            {
                _record = record;
                _writes++;
            }
        }
    }
}

[Collection("Lifecycle stress")]
public class LifecycleSensorSourceTests
{
    private static readonly DateTimeOffset At = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlySet<string> All =
        new HashSet<string> { LifecycleSensorSource.SystemStateId };

    [Fact]
    public void The_sensor_is_off_until_someone_asks_for_it()
    {
        // Its limits are inherent, so it must be a deliberate choice rather than
        // something that quietly starts reporting and then misses a shutdown.
        var (source, _, _) = Build();

        var definition = Assert.Single(source.Definitions);

        Assert.Equal(LifecycleSensorSource.SystemStateId, definition.UniqueId);
        Assert.False(definition.EnabledByDefault);
    }

    [Fact]
    public void The_catalog_description_states_every_limit_up_front()
    {
        var (source, _, _) = Build();

        var description = Assert.Single(source.Definitions).Description;

        Assert.Contains("Best effort", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("may not notify", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("may never reach Home Assistant", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hibernate", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("restart", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recorded locally", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("only trigger", description, StringComparison.OrdinalIgnoreCase);
    }

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

    [Fact]
    public void Repeated_generations_leave_exactly_one_hook_attached()
    {
        var (source, signals, _) = Build();
        var pushes = 0;

        const int generations = 25;
        for (var i = 0; i < generations; i++)
        {
            source.Start(() => pushes++);
            source.Stop();
        }

        Assert.Equal(generations, signals.Starts);
        Assert.Equal(generations, signals.Stops);

        // A generation that forgot to unsubscribe would show up here as a leftover
        // handler, and then as one extra callback per generation below.
        Assert.Equal(0, signals.Subscribers);

        source.Start(() => pushes++);
        Assert.Equal(1, signals.Subscribers);

        signals.Raise(new LifecycleSignal(LifecycleTransition.Sleeping, "Suspend"));

        Assert.Equal(1, pushes);
    }

    [Fact]
    public void Starting_twice_without_stopping_does_not_attach_a_second_hook()
    {
        var (source, signals, _) = Build();
        var pushes = 0;

        source.Start(() => pushes++);
        source.Start(() => pushes++);

        Assert.Equal(1, signals.Starts);
        Assert.Equal(1, signals.Subscribers);

        signals.Raise(new LifecycleSignal(LifecycleTransition.Sleeping, "Suspend"));

        Assert.Equal(1, pushes);
    }

    [Fact]
    public void A_signal_storm_reports_one_change_per_transition()
    {
        var (source, signals, _) = Build();
        var pushes = 0;

        source.Start(() => pushes++);

        for (var i = 0; i < 100; i++)
            signals.Raise(new LifecycleSignal(LifecycleTransition.Sleeping, "Suspend"));

        Assert.Equal(1, pushes);

        for (var i = 0; i < 100; i++)
            signals.Raise(LifecycleSignal.Running("Resume"));

        Assert.Equal(2, pushes);
    }

    [Fact]
    public void Stopping_silences_every_later_signal_across_generations()
    {
        var (source, signals, _) = Build();
        var pushes = 0;

        for (var i = 0; i < 25; i++)
        {
            source.Start(() => pushes++);
            source.Stop();

            // Nothing observed after a stop may reach the callback, however many
            // times the source has been through the cycle.
            signals.Raise(new LifecycleSignal(LifecycleTransition.ShuttingDown, "Shutdown"));
            signals.Raise(LifecycleSignal.Running("Resume"));
        }

        Assert.Equal(0, pushes);
        Assert.Equal(0, signals.Subscribers);
    }

    [Fact]
    public void Stopping_twice_is_as_harmless_as_stopping_once()
    {
        var (source, signals, _) = Build();

        source.Start(() => { });
        source.Stop();
        source.Stop();

        Assert.Equal(1, signals.Stops);
        Assert.Equal(0, signals.Subscribers);
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

        public int Starts { get; private set; }

        public int Stops { get; private set; }

        /// <summary>How many handlers are attached, so a leak is visible as a count.</summary>
        public int Subscribers => SignalObserved?.GetInvocationList().Length ?? 0;

        public void Raise(LifecycleSignal signal) => SignalObserved?.Invoke(signal);

        public void Start()
        {
            Stopped = false;
            Starts++;
        }

        public void Stop()
        {
            Stopped = true;
            Stops++;
        }
    }

    private sealed class FakeJournal : ILifecycleJournal
    {
        private LifecycleRecord? _record;

        public LifecycleRecord? Read() => _record;

        public void Write(LifecycleRecord record) => _record = record;
    }
}

[Collection("Lifecycle stress")]
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
        for (var attempt = 0; attempt < 20; attempt++)
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
    public void A_hundred_start_and_stop_generations_leave_no_pump_behind()
    {
        using var lifetime = new MessagePumpLifetime();
        var live = 0;
        var peak = 0;

        for (var generation = 0; generation < 25; generation++)
        {
            Assert.True(lifetime.TryBeginStart());

            // Held until the stop has been requested, so every generation exercises
            // the order that used to leak: the request arrives before the window.
            using var released = new ManualResetEventSlim(false);
            var pump = new Thread(() =>
            {
                var now = Interlocked.Increment(ref live);
                InterlockedMax(ref peak, now);
                try
                {
                    released.Wait(Wait);
                    if (lifetime.StopRequested) return;
                    lifetime.MarkReady();
                }
                finally
                {
                    Interlocked.Decrement(ref live);
                    lifetime.MarkStopped();
                }
            });
            pump.Start();

            Assert.True(lifetime.RequestStop());
            released.Set();
            Assert.True(lifetime.WaitUntilReady(Wait));
            Assert.True(pump.Join(Wait));

            // Each generation hands the lifetime back exactly as it found it.
            Assert.False(lifetime.IsRunning);
            Assert.False(lifetime.StopRequested);
            Assert.Equal(0, Volatile.Read(ref live));
        }

        // Never two pumps at once, which is what an orphaned generation would mean.
        Assert.Equal(1, Volatile.Read(ref peak));
        Assert.True(lifetime.TryBeginStart());
    }

    [Fact]
    public void A_pump_that_fails_still_hands_the_lifetime_back()
    {
        using var lifetime = new MessagePumpLifetime();

        for (var generation = 0; generation < 25; generation++)
        {
            Assert.True(lifetime.TryBeginStart());

            var pump = new Thread(() =>
            {
                try
                {
                    // Stands for CreateWindowEx failing, or the class registration
                    // throwing: the pump leaves without ever having a window.
                    throw new InvalidOperationException("no window");
                }
                catch (InvalidOperationException)
                {
                }
                finally
                {
                    lifetime.MarkStopped();
                }
            });
            pump.Start();

            Assert.True(lifetime.WaitUntilReady(Wait));
            Assert.True(pump.Join(Wait));
            Assert.False(lifetime.IsRunning);
        }

        Assert.True(lifetime.TryBeginStart());
    }

    [Fact]
    public void A_thread_that_never_ran_does_not_strand_the_next_start()
    {
        using var lifetime = new MessagePumpLifetime();

        for (var generation = 0; generation < 10; generation++)
        {
            Assert.True(lifetime.TryBeginStart());
            Assert.True(lifetime.RequestStop());

            // The stopper found no thread to join, so it reclaims the lifetime.
            lifetime.MarkStopped();

            Assert.True(lifetime.WaitUntilReady(TimeSpan.FromMilliseconds(50)));
            Assert.False(lifetime.IsRunning);
        }

        Assert.True(lifetime.TryBeginStart());
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

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value)
        {
            if (Interlocked.CompareExchange(ref target, value, seen) == seen) return;
        }
    }
}

public class LifecycleSensorAdvisoryTests
{
    [Fact]
    public void Only_switching_the_lifecycle_sensor_on_needs_confirming()
    {
        const string id = LifecycleSensorSource.SystemStateId;

        Assert.True(LifecycleSensorAdvisory.RequiresConfirmation(id, turningOn: true, currentlyEnabled: false));

        // Turning it off warns about nothing, and neither does re-applying a state
        // it already has - a rebuilt list must not nag.
        Assert.False(LifecycleSensorAdvisory.RequiresConfirmation(id, turningOn: false, currentlyEnabled: true));
        Assert.False(LifecycleSensorAdvisory.RequiresConfirmation(id, turningOn: true, currentlyEnabled: true));
        Assert.False(LifecycleSensorAdvisory.RequiresConfirmation(id, turningOn: false, currentlyEnabled: false));
    }

    [Theory]
    [InlineData("battery_state")]
    [InlineData("active")]
    [InlineData("SYSTEM_STATE")]
    [InlineData("")]
    public void No_other_sensor_is_affected(string uniqueId)
    {
        Assert.False(LifecycleSensorAdvisory.IsAdvisedSensor(uniqueId));
        Assert.False(LifecycleSensorAdvisory.RequiresConfirmation(uniqueId, turningOn: true, currentlyEnabled: false));
    }

    [Fact]
    public void The_lifecycle_sensor_carries_the_badge()
    {
        Assert.True(LifecycleSensorAdvisory.IsAdvisedSensor(LifecycleSensorSource.SystemStateId));
        Assert.False(string.IsNullOrWhiteSpace(LifecycleSensorAdvisory.Badge));
    }

    [Fact]
    public void The_warning_names_every_limit_and_offers_a_way_out()
    {
        Assert.Equal("Best-effort Windows lifecycle detection", LifecycleSensorAdvisory.Title);
        Assert.Equal("Enable anyway", LifecycleSensorAdvisory.PrimaryButton);
        Assert.Equal("Cancel", LifecycleSensorAdvisory.CloseButton);

        var message = LifecycleSensorAdvisory.Message;

        Assert.Contains("does not promise", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("may never reach Home Assistant", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hibernate", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("restart", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("local journal", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("only trigger", message, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class FixedClock : IClock
{
    public FixedClock(DateTimeOffset now) => UtcNow = now;

    public DateTimeOffset UtcNow { get; set; }
}
