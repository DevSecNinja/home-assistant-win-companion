using HaCompanion.Core.App;

namespace HaCompanion.Core.Tests;

/// <summary>
/// Covers the races between the user's connection actions and the background
/// route switching that failover performs on its own schedule.
/// </summary>
public class ConnectionLifecycleTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // ---- The primitive -----------------------------------------------------

    [Fact]
    public async Task Start_marks_the_connection_as_wanted()
    {
        using var lifecycle = new ConnectionLifecycle();

        using (await lifecycle.AcquireAsync(LifecycleIntent.Start)) { }

        Assert.True(lifecycle.ConnectionWanted);
    }

    [Theory]
    [InlineData(LifecycleIntent.Stop)]
    [InlineData(LifecycleIntent.Forget)]
    public async Task Ending_the_connection_marks_it_as_unwanted(LifecycleIntent intent)
    {
        using var lifecycle = new ConnectionLifecycle();

        using (await lifecycle.AcquireAsync(LifecycleIntent.Start)) { }
        using (await lifecycle.AcquireAsync(intent)) { }

        Assert.False(lifecycle.ConnectionWanted);
    }

    [Fact]
    public async Task Saving_settings_while_disconnected_does_not_make_the_connection_wanted()
    {
        using var lifecycle = new ConnectionLifecycle();

        using (await lifecycle.AcquireAsync(LifecycleIntent.Reconfigure)) { }

        Assert.False(lifecycle.ConnectionWanted);
    }

    [Fact]
    public async Task Saving_settings_while_connected_keeps_the_connection_wanted()
    {
        using var lifecycle = new ConnectionLifecycle();

        using (await lifecycle.AcquireAsync(LifecycleIntent.Start)) { }
        using (await lifecycle.AcquireAsync(LifecycleIntent.Reconfigure)) { }

        Assert.True(lifecycle.ConnectionWanted);
    }

    [Fact]
    public async Task A_route_switch_is_refused_when_no_connection_is_wanted()
    {
        using var lifecycle = new ConnectionLifecycle();

        Assert.Null(await lifecycle.TryAcquireRouteSwitchAsync());

        using (await lifecycle.AcquireAsync(LifecycleIntent.Start)) { }
        using (await lifecycle.AcquireAsync(LifecycleIntent.Stop)) { }

        Assert.Null(await lifecycle.TryAcquireRouteSwitchAsync());
    }

    [Fact]
    public async Task A_route_switch_is_dropped_rather_than_queued_behind_a_user_action()
    {
        using var lifecycle = new ConnectionLifecycle();
        using (await lifecycle.AcquireAsync(LifecycleIntent.Start)) { }

        using var held = await lifecycle.AcquireAsync(LifecycleIntent.Reconfigure);

        Assert.Null(await lifecycle.TryAcquireRouteSwitchAsync());
    }

    [Fact]
    public async Task A_route_switch_does_not_change_the_generation()
    {
        using var lifecycle = new ConnectionLifecycle();
        using (await lifecycle.AcquireAsync(LifecycleIntent.Start)) { }

        var before = lifecycle.Epoch;
        using (await lifecycle.TryAcquireRouteSwitchAsync()) { }

        Assert.Equal(before, lifecycle.Epoch);
    }

    [Fact]
    public async Task A_user_action_pre_empts_the_transition_in_progress()
    {
        using var lifecycle = new ConnectionLifecycle();
        var running = await lifecycle.AcquireAsync(LifecycleIntent.Start);

        var stop = Task.Run(async () =>
        {
            using var _ = await lifecycle.AcquireAsync(LifecycleIntent.Stop);
        });

        // The pre-emption is what lets the running transition notice and bail out
        // rather than making the user wait for its network calls.
        await WaitFor(() => running.Token.IsCancellationRequested);
        Assert.False(running.IsCurrent);

        running.Dispose();
        await stop.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Releasing_a_lease_twice_does_not_free_the_lifecycle_twice()
    {
        using var lifecycle = new ConnectionLifecycle();

        var lease = await lifecycle.AcquireAsync(LifecycleIntent.Start);
        lease.Dispose();
        lease.Dispose();

        // A second free would let two transitions run at once, so prove the next
        // acquire is genuinely exclusive.
        using var next = await lifecycle.AcquireAsync(LifecycleIntent.Reconfigure).WaitAsync(Timeout);
        Assert.Null(await lifecycle.TryAcquireRouteSwitchAsync());
    }

    [Fact]
    public async Task A_route_switch_cannot_be_taken_as_a_user_action()
    {
        using var lifecycle = new ConnectionLifecycle();

        await Assert.ThrowsAsync<ArgumentException>(
            () => lifecycle.AcquireAsync(LifecycleIntent.RouteSwitch));
    }

    // ---- The races the lifecycle exists to prevent -------------------------

    [Fact]
    public async Task Disconnecting_during_a_route_switch_leaves_the_connection_down()
    {
        await using var host = new Host();
        await host.StartAsync();

        var restart = await host.BeginPausedRouteSwitchAsync();

        await host.StopAsync().WaitAsync(Timeout);
        await restart.WaitAsync(Timeout);

        Assert.Equal(0, host.LiveManagers);
        Assert.False(host.IsConnected);
        Assert.Equal(1, host.PeakLiveManagers);
    }

    [Fact]
    public async Task Reconnecting_during_a_route_switch_leaves_exactly_one_connection()
    {
        await using var host = new Host();
        await host.StartAsync();

        var restart = await host.BeginPausedRouteSwitchAsync();

        await host.ReconnectAsync().WaitAsync(Timeout);
        await restart.WaitAsync(Timeout);

        Assert.Equal(1, host.LiveManagers);
        Assert.Equal(1, host.PeakLiveManagers);
        Assert.Equal(host.Builds, host.Teardowns + host.LiveManagers);
    }

    [Fact]
    public async Task Removing_the_server_during_a_route_switch_does_not_restore_the_settings()
    {
        await using var host = new Host();
        await host.StartAsync();

        var restart = await host.BeginPausedRouteSwitchAsync();

        await host.ForgetAsync().WaitAsync(Timeout);
        await restart.WaitAsync(Timeout);

        Assert.False(host.SettingsExist);
        Assert.Equal(0, host.LiveManagers);
    }

    [Fact]
    public async Task A_route_switch_after_the_server_is_removed_does_nothing()
    {
        await using var host = new Host();
        await host.StartAsync();
        await host.ForgetAsync();

        await host.RouteSwitchAsync();

        Assert.False(host.SettingsExist);
        Assert.Equal(0, host.LiveManagers);
        Assert.Equal(1, host.Builds); // only the initial start ever built
    }

    [Fact]
    public async Task Concurrent_lifecycle_changes_never_leave_two_live_connections()
    {
        await using var host = new Host();
        await host.StartAsync();

        var work = new List<Task>();
        for (var i = 0; i < 40; i++)
        {
            work.Add(host.RouteSwitchAsync());
            work.Add(host.StartAsync());
            work.Add(host.StopAsync());
            work.Add(host.ReconnectAsync());
        }

        await Task.WhenAll(work).WaitAsync(Timeout);

        Assert.Equal(1, host.PeakLiveManagers);
        Assert.Equal(host.Builds, host.Teardowns + host.LiveManagers);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition was never met.");
            await Task.Yield();
        }
    }

    /// <summary>
    /// Stands in for AppController: the same transitions over the same lifecycle,
    /// with the network calls replaced by a build step the test can pause inside.
    /// </summary>
    private sealed class Host : IAsyncDisposable
    {
        private readonly ConnectionLifecycle _lifecycle = new();
        private readonly Lock _sync = new();
        private object? _connection;

        /// <summary>Pauses the next build only, so a test can race it precisely.</summary>
        private TaskCompletionSource? _pause;
        private TaskCompletionSource _buildReached = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool SettingsExist { get; private set; } = true;
        public int LiveManagers { get; private set; }
        public int PeakLiveManagers { get; private set; }
        public int Builds { get; private set; }
        public int Teardowns { get; private set; }
        public bool IsConnected => _connection is not null;

        public async Task StartAsync()
        {
            using var lease = await _lifecycle.AcquireAsync(LifecycleIntent.Start);
            if (!SettingsExist) return;
            await BuildAsync(lease.Token);
        }

        public Task ReconnectAsync() => StartAsync();

        public async Task StopAsync()
        {
            using var lease = await _lifecycle.AcquireAsync(LifecycleIntent.Stop);
            TearDown();
        }

        public async Task ForgetAsync()
        {
            using var lease = await _lifecycle.AcquireAsync(LifecycleIntent.Forget);
            TearDown();
            SettingsExist = false;
        }

        /// <summary>Background failover moving the connection to another address.</summary>
        public async Task RouteSwitchAsync()
        {
            using var lease = await _lifecycle.TryAcquireRouteSwitchAsync();
            if (lease is null) return;

            try
            {
                // Mirrors the real restart: tear down, then rebuild on the new address.
                TearDown();
                SaveSettings();
                await BuildAsync(lease.Token);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a user action, which now owns the connection.
            }
        }

        /// <summary>
        /// Starts a route switch and returns once it has torn the connection down
        /// and is blocked inside the rebuild - the exact window a user action used
        /// to slip through.
        /// </summary>
        public async Task<Task> BeginPausedRouteSwitchAsync()
        {
            _pause = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _buildReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var switching = Task.Run(RouteSwitchAsync);
            await _buildReached.Task.WaitAsync(Timeout);
            return switching;
        }

        private async Task BuildAsync(CancellationToken ct)
        {
            var pause = Interlocked.Exchange(ref _pause, null);
            _buildReached.TrySetResult();

            if (pause is not null) await pause.Task.WaitAsync(ct);
            ct.ThrowIfCancellationRequested();

            // The defensive invariant from BuildAndStartAsync: never leave a second
            // live manager behind, even if a caller forgot to tear down first.
            TearDown();
            _connection = new object();

            lock (_sync)
            {
                Builds++;
                LiveManagers++;
                PeakLiveManagers = Math.Max(PeakLiveManagers, LiveManagers);
            }

            SaveSettings();
        }

        private void TearDown()
        {
            if (_connection is null) return;
            _connection = null;
            lock (_sync)
            {
                Teardowns++;
                LiveManagers--;
            }
        }

        // Background work writing settings back out is how a removed server used to
        // come back from the dead.
        private void SaveSettings() => SettingsExist = true;

        public ValueTask DisposeAsync()
        {
            _lifecycle.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
