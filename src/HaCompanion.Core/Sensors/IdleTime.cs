namespace HaCompanion.Core.Sensors;

/// <summary>
/// Idle-time arithmetic, kept separate from the Win32 call so the awkward parts -
/// tick-count wraparound and the threshold boundary - can be tested.
/// </summary>
public static class IdleTime
{
    /// <summary>
    /// Never treat the machine as idle faster than this, however the user has
    /// configured the threshold. A very short threshold would flap constantly.
    /// </summary>
    public const int MinimumThresholdSeconds = 30;

    /// <summary>
    /// Time since the last input, from two 32-bit millisecond tick counts.
    /// </summary>
    /// <remarks>
    /// <c>GetTickCount</c> and <c>LASTINPUTINFO.dwTime</c> both wrap every ~49.7
    /// days. Unchecked subtraction in 32-bit arithmetic stays correct across the
    /// wrap; widening to a signed 64-bit type first does not, and would produce a
    /// ~49 day idle time.
    /// </remarks>
    public static TimeSpan Since(uint nowTicks, uint lastInputTicks) =>
        TimeSpan.FromMilliseconds(unchecked(nowTicks - lastInputTicks));

    /// <summary>Whether an idle duration has reached the configured threshold.</summary>
    public static bool IsIdle(TimeSpan idleFor, int thresholdSeconds) =>
        idleFor >= TimeSpan.FromSeconds(Math.Max(MinimumThresholdSeconds, thresholdSeconds));
}
