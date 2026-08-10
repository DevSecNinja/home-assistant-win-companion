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

    private sealed class CapabilityProbe
    {
        private readonly Channel<bool> _reads = Channel.CreateUnbounded<bool>();
        private bool _active;

        public bool Active
        {
            get => Volatile.Read(ref _active);
            set => Volatile.Write(ref _active, value);
        }

        public bool Read(string capability)
        {
            Assert.Equal("microphone", capability);
            _reads.Writer.TryWrite(true);
            return Active;
        }

        public async Task WaitForReadAsync() =>
            await _reads.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
    }
}
