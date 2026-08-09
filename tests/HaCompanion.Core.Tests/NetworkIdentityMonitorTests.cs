using HaCompanion.Core.Sensors;

namespace HaCompanion.Core.Tests;

/// <summary>
/// Reliability and resource-usage regressions for the network sensors' OS hook:
/// exactly one subscription, no work while nothing is enabled, no captures in
/// parallel, and no push for a change that did not change anything.
/// </summary>
public class NetworkIdentityMonitorTests
{
    private static readonly NetworkIdentity Wired =
        new(NetworkClassifier.Ethernet, "192.168.1.20", "2001:db8::20", "AA:BB:CC:DD:EE:FF");

    private static readonly NetworkIdentity Wireless =
        new(NetworkClassifier.WiFi, "192.168.1.30", "2001:db8::30", "11:22:33:44:55:66");

    [Fact]
    public void Repeated_start_never_stacks_subscriptions()
    {
        var watcher = new RecordingWatcher();
        var monitor = new NetworkIdentityMonitor(watcher, new Captures().Capture, () => NetworkCaptureScope.Full);

        monitor.Start(() => { });
        monitor.Start(() => { });
        monitor.Start(() => { });

        Assert.Equal(1, watcher.StartCount);
        Assert.Equal(0, watcher.StopCount);
    }

    [Fact]
    public void Repeated_stop_releases_the_subscription_once()
    {
        var watcher = new RecordingWatcher();
        var monitor = new NetworkIdentityMonitor(watcher, new Captures().Capture, () => NetworkCaptureScope.Full);

        monitor.Start(() => { });
        monitor.Stop();
        monitor.Stop();
        monitor.Stop();

        Assert.Equal(1, watcher.StopCount);
        Assert.False(watcher.IsSubscribed);
    }

    [Fact]
    public void Stopping_without_starting_touches_nothing()
    {
        var watcher = new RecordingWatcher();
        var monitor = new NetworkIdentityMonitor(watcher, new Captures().Capture, () => NetworkCaptureScope.Full);

        monitor.Stop();

        Assert.Equal(0, watcher.StartCount);
        Assert.Equal(0, watcher.StopCount);
    }

    [Fact]
    public void Restart_cycles_hold_exactly_one_subscription_at_a_time()
    {
        var watcher = new RecordingWatcher();
        var monitor = new NetworkIdentityMonitor(watcher, new Captures().Capture, () => NetworkCaptureScope.Full);

        for (var cycle = 0; cycle < 25; cycle++)
        {
            monitor.Start(() => { });
            Assert.True(watcher.IsSubscribed);
            monitor.Stop();
            Assert.False(watcher.IsSubscribed);
        }

        Assert.Equal(25, watcher.StartCount);
        Assert.Equal(25, watcher.StopCount);
    }

    [Fact]
    public void A_change_delivered_after_stop_does_no_work_and_pushes_nothing()
    {
        var watcher = new RecordingWatcher();
        var captures = new Captures(Wired);
        var pushes = 0;
        var monitor = new NetworkIdentityMonitor(watcher, captures.Capture, () => NetworkCaptureScope.Full);

        monitor.Start(() => pushes++);
        monitor.Stop();

        // The OS can still deliver a callback that was already in flight.
        watcher.RaiseLate();

        Assert.Equal(0, captures.Count);
        Assert.Equal(0, pushes);
    }

    [Fact]
    public void Restarting_reports_the_first_change_again()
    {
        var watcher = new RecordingWatcher();
        var captures = new Captures(Wired);
        var pushes = 0;
        var monitor = new NetworkIdentityMonitor(watcher, captures.Capture, () => NetworkCaptureScope.Full);

        monitor.Start(() => pushes++);
        watcher.Raise();
        watcher.Raise();
        Assert.Equal(1, pushes);

        monitor.Stop();
        monitor.Start(() => pushes++);
        watcher.Raise();

        Assert.Equal(2, pushes);
    }

    [Fact]
    public void Nothing_is_enumerated_while_no_network_sensor_is_enabled()
    {
        var watcher = new RecordingWatcher();
        var captures = new Captures(Wired);
        var pushes = 0;
        var monitor = new NetworkIdentityMonitor(watcher, captures.Capture, () => NetworkCaptureScope.None);

        monitor.Start(() => pushes++);
        for (var change = 0; change < 20; change++) watcher.Raise();
        var reading = monitor.Read(NetworkCaptureScope.None);

        Assert.Equal(0, captures.Count);
        Assert.Equal(0, pushes);
        Assert.Equal(NetworkIdentity.NotConnected, reading);
    }

    [Fact]
    public void Connection_type_alone_never_reaches_an_identifier()
    {
        var watcher = new RecordingWatcher();
        var captures = new Captures(Wired);
        var monitor = new NetworkIdentityMonitor(
            watcher, captures.Capture, () => NetworkCaptureScope.ConnectionTypeOnly);

        monitor.Start(() => { });
        watcher.Raise();
        monitor.Read(NetworkCaptureScope.ConnectionTypeOnly);

        Assert.NotEmpty(captures.Scopes);
        Assert.All(captures.Scopes, scope => Assert.Equal(NetworkCaptureScope.ConnectionTypeOnly, scope));
    }

    [Fact]
    public void A_grouped_read_takes_exactly_one_snapshot()
    {
        var captures = new Captures(Wired);
        var monitor = new NetworkIdentityMonitor(
            new RecordingWatcher(), captures.Capture, () => NetworkCaptureScope.Full);

        var identity = monitor.Read(NetworkCaptureScope.Full);

        Assert.Equal(1, captures.Count);
        Assert.Equal(Wired, identity);
    }

    [Fact]
    public void A_burst_of_identical_changes_publishes_once()
    {
        var watcher = new RecordingWatcher();
        var captures = new Captures(Wired);
        var pushes = 0;
        var monitor = new NetworkIdentityMonitor(watcher, captures.Capture, () => NetworkCaptureScope.Full);

        monitor.Start(() => pushes++);
        for (var change = 0; change < 50; change++) watcher.Raise();

        Assert.Equal(1, pushes);
    }

    [Fact]
    public void Changes_arriving_during_a_capture_collapse_into_one_more_pass()
    {
        var watcher = new RecordingWatcher();
        var captures = new Captures(Wired);
        var pushes = 0;
        var monitor = new NetworkIdentityMonitor(watcher, captures.Capture, () => NetworkCaptureScope.Full);
        var stormed = false;

        captures.OnCapture = () =>
        {
            if (stormed) return;
            stormed = true;
            for (var change = 0; change < 10; change++) watcher.Raise();
        };

        monitor.Start(() => pushes++);
        watcher.Raise();

        Assert.Equal(2, captures.Count); // the original capture plus a single coalesced re-check
        Assert.Equal(1, pushes);
    }

    [Fact]
    public void A_real_change_is_published_after_an_unchanged_burst()
    {
        var watcher = new RecordingWatcher();
        var captures = new Captures(Wired);
        var pushes = 0;
        var monitor = new NetworkIdentityMonitor(watcher, captures.Capture, () => NetworkCaptureScope.Full);

        monitor.Start(() => pushes++);
        watcher.Raise();
        watcher.Raise();
        captures.Identity = Wireless;
        watcher.Raise();

        Assert.Equal(2, pushes);
    }

    [Fact]
    public void A_change_matching_the_last_read_value_is_not_published()
    {
        var watcher = new RecordingWatcher();
        var captures = new Captures(Wired);
        var pushes = 0;
        var monitor = new NetworkIdentityMonitor(watcher, captures.Capture, () => NetworkCaptureScope.Full);

        monitor.Start(() => pushes++);
        monitor.Read(NetworkCaptureScope.Full);
        watcher.Raise();

        Assert.Equal(0, pushes);
    }

    [Fact]
    public void Concurrent_changes_are_serialised_and_publish_once()
    {
        var watcher = new RecordingWatcher();
        var captures = new Captures(Wired) { CaptureDelay = TimeSpan.FromMilliseconds(2) };
        var pushes = 0;
        var monitor = new NetworkIdentityMonitor(
            watcher, captures.Capture, () => NetworkCaptureScope.Full);

        monitor.Start(() => Interlocked.Increment(ref pushes));
        Parallel.For(0, 64, _ => watcher.Raise());

        Assert.Equal(1, captures.MaxConcurrent);
        Assert.InRange(captures.Count, 1, 64);
        Assert.Equal(1, Volatile.Read(ref pushes));
    }

    [Fact]
    public void Stopping_during_a_capture_discards_the_push()
    {
        var watcher = new RecordingWatcher();
        var captures = new Captures(Wired);
        var pushes = 0;
        var monitor = new NetworkIdentityMonitor(watcher, captures.Capture, () => NetworkCaptureScope.Full);

        captures.OnCapture = () => monitor.Stop();

        monitor.Start(() => pushes++);
        watcher.Raise();

        Assert.Equal(1, captures.Count);
        Assert.Equal(0, pushes);
        Assert.Equal(1, watcher.StopCount);
    }

    [Fact]
    public void Reads_keep_working_after_the_hook_is_released()
    {
        var watcher = new RecordingWatcher();
        var captures = new Captures(Wired);
        var monitor = new NetworkIdentityMonitor(watcher, captures.Capture, () => NetworkCaptureScope.Full);

        monitor.Start(() => { });
        monitor.Stop();

        Assert.Equal(Wired, monitor.Read(NetworkCaptureScope.Full));
        Assert.Equal(1, captures.Count);
    }

    private sealed class RecordingWatcher : INetworkChangeWatcher
    {
        private Action? _handler;
        private Action? _lastHandler;

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public bool IsSubscribed => _handler is not null;

        public void Start(Action onChanged)
        {
            StartCount++;
            _handler = onChanged;
            _lastHandler = onChanged;
        }

        public void Stop()
        {
            StopCount++;
            _handler = null;
        }

        public void Raise() => _handler?.Invoke();

        /// <summary>An OS callback that was already in flight when the hook was released.</summary>
        public void RaiseLate() => _lastHandler?.Invoke();
    }

    private sealed class Captures(NetworkIdentity? identity = null)
    {
        private readonly List<NetworkCaptureScope> _scopes = [];
        private readonly object _gate = new();
        private int _concurrent;
        private int _maxConcurrent;

        public NetworkIdentity Identity { get; set; } = identity ?? NetworkIdentity.NotConnected;

        public TimeSpan CaptureDelay { get; init; }

        public Action? OnCapture { get; set; }

        public int Count
        {
            get { lock (_gate) return _scopes.Count; }
        }

        public IReadOnlyList<NetworkCaptureScope> Scopes
        {
            get { lock (_gate) return _scopes.ToList(); }
        }

        public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);

        public NetworkIdentity Capture(NetworkCaptureScope scope)
        {
            var running = Interlocked.Increment(ref _concurrent);
            InterlockedMax(ref _maxConcurrent, running);

            try
            {
                lock (_gate) _scopes.Add(scope);
                if (CaptureDelay > TimeSpan.Zero) Thread.Sleep(CaptureDelay);
                OnCapture?.Invoke();
                return Identity;
            }
            finally
            {
                Interlocked.Decrement(ref _concurrent);
            }
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            while (value > (current = Volatile.Read(ref target)))
            {
                if (Interlocked.CompareExchange(ref target, value, current) == current) return;
            }
        }
    }
}
