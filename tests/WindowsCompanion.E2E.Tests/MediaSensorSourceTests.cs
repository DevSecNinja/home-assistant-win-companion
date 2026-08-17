using System.Threading.Channels;
using WindowsCompanion.Core.Sensors;
using WindowsCompanion.E2E.Tests.Fixtures;
using WindowsCompanion_App.Services;

namespace WindowsCompanion.E2E.Tests;

[Collection(CompanionJourneyCollection.Name)]
public sealed class MediaSensorSourceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void Media_polling_uses_a_two_second_poll_interval()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), MediaSensorSource.PollInterval);
    }

    [Fact]
    public async Task Track_changes_push_once_while_unchanged_polls_stay_local()
    {
        var preferences = new SensorPreferences();
        preferences.Set(MediaSensorSource.NowPlayingId, true);
        var probe = new MediaProbe();
        using var changed = new SemaphoreSlim(0);
        var notifications = 0;
        var source = new MediaSensorSource(
            preferences,
            probe.CaptureAsync,
            TimeSpan.FromMilliseconds(10));

        try
        {
            source.Start(() =>
            {
                Interlocked.Increment(ref notifications);
                changed.Release();
            });
            await probe.WaitForReadAsync();
            await probe.WaitForReadAsync();

            probe.Snapshot = new MediaSnapshot("New Track", "New Artist", "Player", MediaPlaybackStatus.Playing);
            await changed.WaitAsync(Timeout);

            await probe.WaitForReadAsync();
            await probe.WaitForReadAsync();
            Assert.Equal(1, Volatile.Read(ref notifications));
        }
        finally
        {
            source.Stop();
        }
    }

    [Fact]
    public async Task Disabling_the_last_media_sensor_cancels_collection_and_stops_polling()
    {
        var preferences = new SensorPreferences();
        var probe = new BlockingMediaProbe();
        var source = new MediaSensorSource(
            preferences,
            probe.CaptureAsync,
            TimeSpan.FromMilliseconds(10));
        var catalog = new SensorCatalog([source], preferences);
        catalog.Start(() => { });

        try
        {
            catalog.SetEnabled(MediaSensorSource.NowPlayingId, true);
            await probe.Entered.Task.WaitAsync(Timeout);

            catalog.SetEnabled(MediaSensorSource.NowPlayingId, false);

            await probe.Cancelled.Task.WaitAsync(Timeout);
            await Task.Delay(100);
            Assert.Equal(2, Volatile.Read(ref probe.ReadCount));
        }
        finally
        {
            catalog.Stop();
        }
    }

    [Fact]
    public async Task Enabling_only_media_playing_never_requests_now_playing_metadata()
    {
        var preferences = new SensorPreferences();
        preferences.Set(MediaSensorSource.PlayingId, true);
        var probe = new ScopeRecordingProbe();
        using var read = new SemaphoreSlim(0);
        var source = new MediaSensorSource(
            preferences,
            (requested, cancellationToken) =>
            {
                probe.Record(requested);
                read.Release();
                return Task.FromResult(MediaSnapshot.Empty);
            },
            TimeSpan.FromMilliseconds(10));

        try
        {
            source.Start(() => { });
            await read.WaitAsync(Timeout);
            await read.WaitAsync(Timeout);

            Assert.All(probe.Requests, request => Assert.DoesNotContain(MediaSensorSource.NowPlayingId, request));
            Assert.All(probe.Requests, request => Assert.Contains(MediaSensorSource.PlayingId, request));
        }
        finally
        {
            source.Stop();
        }
    }

    [Fact]
    public async Task Enabling_a_media_sensor_publishes_the_freshly_captured_reading_immediately()
    {
        var preferences = new SensorPreferences();
        // The first capture (returned Empty) is consumed by media_playing
        // starting the source. Only the later Now Playing refresh can produce
        // the second, fresh snapshot - so a broken RefreshAsync leaves the
        // reading at "nothing playing" and fails this test.
        var probe = new SequencedMediaProbe(
            MediaSnapshot.Empty,
            new MediaSnapshot("Fresh Track", "Fresh Artist", "Fresh Player", MediaPlaybackStatus.Playing));
        var source = new MediaSensorSource(preferences, probe.CaptureAsync, TimeSpan.FromMinutes(10));
        var catalog = new SensorCatalog([source], preferences);
        catalog.Start(() => { });

        try
        {
            catalog.SetEnabled(MediaSensorSource.PlayingId, true);
            await probe.WaitForCallsAsync(1);

            var preview = await catalog.SetEnabledAndRefreshAsync(MediaSensorSource.NowPlayingId, true);
            Assert.Equal("Fresh Track", preview);

            var reading = catalog.Read(new SensorReadContext("Test"))
                .Single(sensor => sensor.UniqueId == MediaSensorSource.NowPlayingId);
            Assert.Equal("Fresh Track", reading.State);
        }
        finally
        {
            catalog.Stop();
        }
    }

    private sealed class SequencedMediaProbe(MediaSnapshot first, MediaSnapshot afterFirst)
    {
        private readonly Channel<bool> _calls = Channel.CreateUnbounded<bool>();
        private int _callCount;

        public Task<MediaSnapshot> CaptureAsync(IReadOnlySet<string> requested, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _callCount);
            _calls.Writer.TryWrite(true);
            return Task.FromResult(call == 1 ? first : afterFirst);
        }

        public async Task WaitForCallsAsync(int count)
        {
            for (var i = 0; i < count; i++)
                await _calls.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        }
    }

    [Fact]
    public async Task Refreshing_while_a_narrower_scheduled_poll_is_in_flight_still_captures_the_new_scope()
    {
        var preferences = new SensorPreferences();
        var probe = new GatedMediaProbe();
        var source = new MediaSensorSource(preferences, probe.CaptureAsync, TimeSpan.FromMinutes(10));
        var catalog = new SensorCatalog([source], preferences);
        catalog.Start(() => { });

        try
        {
            // Enabling media_playing starts the source, whose immediate
            // Scheduled tick takes an enabled-ids snapshot of {media_playing}
            // only and then blocks mid-capture.
            catalog.SetEnabled(MediaSensorSource.PlayingId, true);
            await probe.FirstCaptureStarted.Task.WaitAsync(Timeout);

            // The Now Playing refresh has to join that already in-flight,
            // narrower-scoped capture (SensorPollLoop's single-flight gate),
            // so a naive RefreshAsync would settle for its stale result. This
            // release happens after the refresh call is already blocked
            // inside RunOnceAsync, awaiting the very same in-flight capture.
            var refreshTask = catalog.SetEnabledAndRefreshAsync(MediaSensorSource.NowPlayingId, true);
            probe.ReleaseFirstCapture();

            var preview = await refreshTask.WaitAsync(Timeout);
            Assert.Equal("Fresh Track", preview);

            var reading = catalog.Read(new SensorReadContext("Test"))
                .Single(sensor => sensor.UniqueId == MediaSensorSource.NowPlayingId);
            Assert.Equal("Fresh Track", reading.State);
            Assert.True(Volatile.Read(ref probe.CallCount) >= 2, "the stale, joined capture must be followed by a fresh one");
        }
        finally
        {
            catalog.Stop();
        }
    }

    private sealed class GatedMediaProbe
    {
        private readonly SemaphoreSlim _release = new(0);

        public TaskCompletionSource FirstCaptureStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount;

        public async Task<MediaSnapshot> CaptureAsync(IReadOnlySet<string> requested, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref CallCount);

            if (call == 1)
            {
                FirstCaptureStarted.TrySetResult();
                await _release.WaitAsync(cancellationToken).ConfigureAwait(false);
                return MediaSnapshot.Empty;
            }

            return new MediaSnapshot("Fresh Track", "Fresh Artist", "Fresh Player", MediaPlaybackStatus.Playing);
        }

        public void ReleaseFirstCapture() => _release.Release();
    }

    private sealed class ScopeRecordingProbe
    {
        private readonly List<IReadOnlySet<string>> _requests = [];

        public IReadOnlyList<IReadOnlySet<string>> Requests
        {
            get { lock (this) return _requests.ToList(); }
        }

        public void Record(IReadOnlySet<string> requested)
        {
            lock (this) _requests.Add(requested);
        }
    }

    private sealed class MediaProbe
    {
        private readonly Channel<bool> _reads = Channel.CreateUnbounded<bool>();
        private MediaSnapshot _snapshot = MediaSnapshot.Empty;

        public MediaSnapshot Snapshot
        {
            get { lock (this) return _snapshot; }
            set { lock (this) _snapshot = value; }
        }

        public Task<MediaSnapshot> CaptureAsync(IReadOnlySet<string> requested, CancellationToken cancellationToken)
        {
            _reads.Writer.TryWrite(true);
            return Task.FromResult(Snapshot);
        }

        public async Task WaitForReadAsync() =>
            await _reads.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
    }

    private sealed class BlockingMediaProbe
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ReadCount;

        public async Task<MediaSnapshot> CaptureAsync(
            IReadOnlySet<string> requested, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref ReadCount) == 1) return MediaSnapshot.Empty;

            Entered.TrySetResult();
            try
            {
                // A real, synchronous WaitHandle.WaitOne() here would pin a
                // thread-pool thread for as long as the flight is in-flight,
                // which risks thread-pool starvation (and, on a constrained CI
                // runner, a hung/crashed test host) instead of just this test.
                // Task.Delay with an infinite timeout waits the same way but
                // yields the thread back to the pool.
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken)
                    .ConfigureAwait(false);
                return MediaSnapshot.Empty;
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                throw;
            }
        }
    }
}
