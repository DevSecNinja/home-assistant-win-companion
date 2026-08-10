using System.Threading.Channels;
using WindowsCompanion.Core.Sensors;
using WindowsCompanion.E2E.Tests.Fixtures;
using WindowsCompanion_App.Services;

namespace WindowsCompanion.E2E.Tests;

[Collection(CompanionJourneyCollection.Name)]
public sealed class CapabilityUsageSensorSourceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void Capability_activity_uses_a_one_second_poll_interval()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), CapabilityUsageSensorSource.PollInterval);
    }

    [Fact]
    public async Task Activity_changes_push_once_while_unchanged_polls_stay_local()
    {
        var preferences = new SensorPreferences();
        preferences.Set(CapabilityUsageSensorSource.MicrophoneId, true);
        var probe = new CapabilityProbe();
        using var changed = new SemaphoreSlim(0);
        var notifications = 0;
        var source = new CapabilityUsageSensorSource(
            preferences,
            probe.Read,
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

            probe.Active = true;
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
    public async Task Disabling_the_last_capability_cancels_collection_and_stops_polling()
    {
        var preferences = new SensorPreferences();
        var probe = new BlockingCapabilityProbe();
        var source = new CapabilityUsageSensorSource(
            preferences,
            probe.Read,
            TimeSpan.FromMilliseconds(10));
        var catalog = new SensorCatalog([source], preferences);
        catalog.Start(() => { });

        try
        {
            catalog.SetEnabled(CapabilityUsageSensorSource.MicrophoneId, true);
            await probe.Entered.Task.WaitAsync(Timeout);

            catalog.SetEnabled(CapabilityUsageSensorSource.MicrophoneId, false);

            await probe.Cancelled.Task.WaitAsync(Timeout);
            await Task.Delay(100);
            Assert.Equal(2, Volatile.Read(ref probe.ReadCount));
        }
        finally
        {
            catalog.Stop();
        }
    }

    private sealed class CapabilityProbe
    {
        private readonly Channel<bool> _reads = Channel.CreateUnbounded<bool>();
        private bool _active;

        public bool Active
        {
            get => Volatile.Read(ref _active);
            set => Volatile.Write(ref _active, value);
        }

        public bool Read(string capability, CancellationToken cancellationToken)
        {
            Assert.Equal("microphone", capability);
            _reads.Writer.TryWrite(true);
            return Active;
        }

        public async Task WaitForReadAsync() =>
            await _reads.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
    }

    private sealed class BlockingCapabilityProbe
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ReadCount;

        public bool Read(string capability, CancellationToken cancellationToken)
        {
            Assert.Equal("microphone", capability);
            if (Interlocked.Increment(ref ReadCount) == 1) return false;

            Entered.TrySetResult();
            try
            {
                cancellationToken.WaitHandle.WaitOne();
                cancellationToken.ThrowIfCancellationRequested();
                return false;
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                throw;
            }
        }
    }
}
