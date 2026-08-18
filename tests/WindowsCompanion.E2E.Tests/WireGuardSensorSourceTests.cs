using WindowsCompanion.Core.Sensors;
using WindowsCompanion_App.Services;

namespace WindowsCompanion.E2E.Tests;

public sealed class WireGuardSensorSourceTests
{
    private static readonly HashSet<string> Enabled =
        new(StringComparer.Ordinal) { WireGuardSensorSource.StatusId };

    [Fact]
    public void Definition_is_opt_in_sensitive_and_describes_the_handshake_boundary()
    {
        var definition = Assert.Single(new WireGuardSensorSource(
            new FakeProbe(), new FakeWatcher()).Definitions);

        Assert.Equal(WireGuardSensorSource.StatusId, definition.UniqueId);
        Assert.Equal(SensorPrivacy.Sensitive, definition.Privacy);
        Assert.False(definition.EnabledByDefault);
        Assert.Contains("handshake", definition.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Disabled_catalog_preview_does_not_observe_wireguard()
    {
        var probe = new FakeProbe(WireGuardStatus.Connected);
        var source = new WireGuardSensorSource(probe, new FakeWatcher());
        var catalog = new SensorCatalog([source], new SensorPreferences());

        var preview = await catalog.PreviewAsync();

        Assert.Equal("Enable to read WireGuard status", preview[WireGuardSensorSource.StatusId]);
        Assert.Equal(0, probe.ReadCount);
    }

    [Fact]
    public void Read_returns_only_the_requested_diagnostic_sensor()
    {
        var source = new WireGuardSensorSource(
            new FakeProbe(WireGuardStatus.Connected), new FakeWatcher());

        Assert.Empty(source.Read(
            new HashSet<string>(StringComparer.Ordinal),
            SensorReadContext.Periodic));
        var sensor = Assert.Single(source.Read(Enabled, SensorReadContext.Periodic));
        Assert.Equal("wireguard_status", sensor.UniqueId);
        Assert.Equal("sensor", sensor.Type);
        Assert.Equal("connected", sensor.State);
        Assert.Equal("diagnostic", sensor.EntityCategory);
        Assert.Null(sensor.Attributes);
    }

    [Fact]
    public async Task Preview_reads_current_status_without_starting_observation()
    {
        var probe = new FakeProbe(WireGuardStatus.Disconnected);
        var watcher = new FakeWatcher();
        var source = new WireGuardSensorSource(probe, watcher);

        var sensor = Assert.Single(await source.PreviewAsync(Enabled));

        Assert.Equal("disconnected", sensor.State);
        Assert.Equal(1, probe.ReadCount);
        Assert.Equal(0, watcher.StartCount);
    }

    [Fact]
    public void Repeated_start_and_stop_hold_exactly_one_subscription()
    {
        var watcher = new FakeWatcher();
        var source = new WireGuardSensorSource(new FakeProbe(), watcher);

        source.Start(() => { });
        source.Start(() => { });
        source.Stop();
        source.Stop();

        Assert.Equal(1, watcher.StartCount);
        Assert.Equal(1, watcher.StopCount);
    }

    [Fact]
    public void Failed_watcher_start_is_unwound_and_can_be_retried()
    {
        var watcher = new FakeWatcher { FailNextStart = true };
        var source = new WireGuardSensorSource(new FakeProbe(), watcher);

        Assert.Throws<InvalidOperationException>(() => source.Start(() => { }));
        Assert.Equal(1, watcher.StopCount);

        source.Start(() => { });
        source.Stop();

        Assert.Equal(2, watcher.StartCount);
        Assert.Equal(2, watcher.StopCount);
    }

    [Fact]
    public void Network_events_push_only_for_a_real_status_transition()
    {
        var probe = new FakeProbe(WireGuardStatus.Disconnected);
        var watcher = new FakeWatcher();
        var pushes = 0;
        var source = new WireGuardSensorSource(probe, watcher);
        source.Start(() => pushes++);
        source.Read(Enabled, SensorReadContext.Periodic);

        watcher.Raise();
        probe.Status = WireGuardStatus.Connected;
        watcher.Raise();
        watcher.Raise();

        Assert.Equal(1, pushes);
    }

    [Fact]
    public async Task Overlapping_network_events_are_coalesced_without_losing_the_latest_state()
    {
        var probe = new BlockingProbe();
        var watcher = new FakeWatcher();
        var pushes = 0;
        var source = new WireGuardSensorSource(probe, watcher);
        source.Start(() => Interlocked.Increment(ref pushes));
        source.Read(Enabled, SensorReadContext.Periodic);

        var firstEvent = Task.Run(watcher.Raise);
        await probe.WaitUntilBlockedAsync();
        probe.Status = WireGuardStatus.Connected;
        watcher.Raise();
        probe.Release();
        await firstEvent;

        Assert.Equal(1, Volatile.Read(ref pushes));
        Assert.True(probe.ReadCount >= 3);
    }

    [Fact]
    public async Task Observation_started_before_stop_cannot_report_into_a_restarted_source()
    {
        var probe = new BlockingProbe();
        var watcher = new FakeWatcher();
        var stalePushes = 0;
        var source = new WireGuardSensorSource(probe, watcher);
        source.Start(() => stalePushes++);
        source.Read(Enabled, SensorReadContext.Periodic);

        var oldEvent = Task.Run(watcher.Raise);
        await probe.WaitUntilBlockedAsync();
        source.Stop();
        probe.Status = WireGuardStatus.Connected;
        source.Start(() => stalePushes++);
        source.Read(Enabled, SensorReadContext.Periodic);
        probe.Release();
        await oldEvent;
        probe.Status = WireGuardStatus.Disconnected;
        watcher.Raise();

        Assert.Equal(1, stalePushes);
    }

    [Fact]
    public void Callback_delivered_after_stop_does_no_work()
    {
        var probe = new FakeProbe(WireGuardStatus.Disconnected);
        var watcher = new FakeWatcher();
        var pushes = 0;
        var source = new WireGuardSensorSource(probe, watcher);
        source.Start(() => pushes++);
        source.Stop();
        var readsBeforeLateCallback = probe.ReadCount;

        watcher.RaiseLate();

        Assert.Equal(readsBeforeLateCallback, probe.ReadCount);
        Assert.Equal(0, pushes);
    }

    private sealed class FakeProbe(WireGuardStatus status = WireGuardStatus.Unavailable)
        : IWireGuardStatusProbe
    {
        public WireGuardStatus Status { get; set; } = status;
        public int ReadCount { get; private set; }

        public WireGuardStatus Read()
        {
            ReadCount++;
            return Status;
        }
    }

    private sealed class BlockingProbe : IWireGuardStatusProbe
    {
        private readonly TaskCompletionSource _blocked =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _release = new();
        private int _readCount;

        public WireGuardStatus Status { get; set; } = WireGuardStatus.Disconnected;
        public int ReadCount => Volatile.Read(ref _readCount);

        public WireGuardStatus Read()
        {
            var read = Interlocked.Increment(ref _readCount);
            var status = Status;
            if (read == 2)
            {
                _blocked.TrySetResult();
                _release.Wait(TimeSpan.FromSeconds(10));
            }

            return status;
        }

        public Task WaitUntilBlockedAsync() => _blocked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        public void Release() => _release.Set();
    }

    private sealed class FakeWatcher : INetworkChangeWatcher
    {
        private Action? _callback;
        private Action? _lastCallback;

        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public bool FailNextStart { get; set; }

        public void Start(Action onChanged)
        {
            StartCount++;
            _callback = onChanged;
            _lastCallback = onChanged;
            if (FailNextStart)
            {
                FailNextStart = false;
                throw new InvalidOperationException("Injected watcher startup failure.");
            }
        }

        public void Stop()
        {
            StopCount++;
            _callback = null;
        }

        public void Raise() => _callback?.Invoke();
        public void RaiseLate() => _lastCallback?.Invoke();
    }
}
