using HaCompanion.Core.Sensors;
using Xunit;

namespace HaCompanion.Core.Tests;

public class IdleTimeTests
{
    [Fact]
    public void Computes_elapsed_time_between_ticks()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), IdleTime.Since(10_000, 5_000));
    }

    [Fact]
    public void Survives_the_32_bit_tick_count_wraparound()
    {
        // GetTickCount wraps every ~49.7 days. The last input was 1s before the
        // wrap, "now" is 1s after it. Naive widening would report ~49 days idle and
        // wrongly mark an in-use machine as away.
        var now = 1_000u;
        var lastInput = uint.MaxValue - 999u;

        Assert.Equal(TimeSpan.FromSeconds(2), IdleTime.Since(now, lastInput));
    }

    [Fact]
    public void Is_idle_only_once_the_threshold_is_reached()
    {
        Assert.False(IdleTime.IsIdle(TimeSpan.FromSeconds(299), 300));
        Assert.True(IdleTime.IsIdle(TimeSpan.FromSeconds(300), 300));
        Assert.True(IdleTime.IsIdle(TimeSpan.FromSeconds(301), 300));
    }

    [Fact]
    public void Threshold_is_floored_so_it_cannot_flap()
    {
        // A user-configured 1 second would otherwise toggle constantly.
        Assert.False(IdleTime.IsIdle(TimeSpan.FromSeconds(5), thresholdSeconds: 1));
        Assert.True(IdleTime.IsIdle(TimeSpan.FromSeconds(IdleTime.MinimumThresholdSeconds), 1));
    }
}
