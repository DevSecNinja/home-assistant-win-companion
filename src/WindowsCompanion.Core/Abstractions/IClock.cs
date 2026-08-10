namespace WindowsCompanion.Core.Abstractions;

/// <summary>Abstraction over the system clock to keep time-based logic testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    long GetTimestamp() => UtcNow.UtcTicks;

    TimeSpan GetElapsedTime(long startingTimestamp) =>
        TimeSpan.FromTicks(UtcNow.UtcTicks - startingTimestamp);

    Task DelayAsync(TimeSpan delay, CancellationToken ct = default) =>
        Task.Delay(delay, ct);
}

/// <summary>Default clock backed by the system time.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public long GetTimestamp() => TimeProvider.System.GetTimestamp();

    public TimeSpan GetElapsedTime(long startingTimestamp) =>
        TimeProvider.System.GetElapsedTime(startingTimestamp);

    public Task DelayAsync(TimeSpan delay, CancellationToken ct = default) =>
        Task.Delay(delay, ct);
}
