namespace WindowsCompanion.Core.Sensors;

/// <summary>Why a poll is happening.</summary>
public enum SensorPollReason
{
    /// <summary>The loop's own timer, or the first tick after starting.</summary>
    Scheduled,

    /// <summary>An explicit refresh, e.g. before a user-triggered push.</summary>
    Requested
}

/// <summary>
/// Owns the start/stop lifetime of a polled sensor source and guarantees that
/// only one collection runs at a time.
/// </summary>
/// <remarks>
/// Every polled source needs the same four things: a cancellation source that is
/// created on start and torn down exactly once, a single-flight gate so a manual
/// refresh cannot run concurrently with the timer, a refresh that is cancelled if
/// the sensor is switched off mid-flight, and a loop that survives a transient
/// failure instead of dying silently. Hand-rolling that per source is how sources
/// end up leaking a poller after a stop/start cycle or throwing
/// <see cref="ObjectDisposedException"/> from a cancelled refresh, so it lives
/// here once and is unit tested.
/// </remarks>
public sealed class SensorPollLoop
{
    private readonly Func<SensorPollReason, CancellationToken, Task> _tick;
    private readonly TimeSpan _interval;
    private readonly SemaphoreSlim _single = new(1, 1);
    private readonly object _gate = new();
    private CancellationTokenSource? _lifetime;

    public SensorPollLoop(Func<SensorPollReason, CancellationToken, Task> tick, TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(tick);
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), "The poll interval must be positive.");

        _tick = tick;
        _interval = interval;
    }

    /// <summary>Whether a poller is currently owned by this loop.</summary>
    public bool IsRunning
    {
        get { lock (_gate) return _lifetime is not null; }
    }

    /// <summary>
    /// Starts polling: one tick immediately, then one per interval. Calling this
    /// while already running does nothing, so a source cannot end up with two
    /// pollers after its sensors are toggled.
    /// </summary>
    public void Start()
    {
        CancellationTokenSource lifetime;
        lock (_gate)
        {
            if (_lifetime is not null) return;
            lifetime = new CancellationTokenSource();
            _lifetime = lifetime;
        }

        _ = RunAsync(lifetime);
    }

    /// <summary>
    /// Stops polling and cancels any collection in flight. Idempotent, and safe
    /// to follow with another <see cref="Start"/>.
    /// </summary>
    public void Stop()
    {
        CancellationTokenSource? lifetime;
        lock (_gate)
        {
            lifetime = _lifetime;
            _lifetime = null;
        }

        // The loop owns disposal: cancelling here and disposing there is what
        // keeps a concurrent refresh from touching a disposed source.
        lifetime?.Cancel();
    }

    /// <summary>
    /// Runs one collection now, sharing the single-flight gate with the timer and
    /// giving up quietly if the loop is stopped while it is in flight.
    /// </summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? lifetime;
        lock (_gate) lifetime = _lifetime;

        CancellationTokenSource? linked = null;
        if (lifetime is not null)
        {
            try
            {
                linked = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, lifetime.Token);
            }
            catch (ObjectDisposedException)
            {
                // The loop stopped and tore the lifetime down while we were linking.
            }
        }

        if (linked is null)
        {
            await ExecuteAsync(SensorPollReason.Requested, cancellationToken).ConfigureAwait(false);
            return;
        }

        using (linked)
        {
            try
            {
                await ExecuteAsync(SensorPollReason.Requested, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The sensor was switched off during the refresh; not a failure.
            }
        }
    }

    private async Task RunAsync(CancellationTokenSource lifetime)
    {
        var cancellationToken = lifetime.Token;
        try
        {
            await TickAsync(cancellationToken).ConfigureAwait(false);

            using var timer = new PeriodicTimer(_interval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                await TickAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
        {
            // Raced with Stop while waiting on the timer; there is nothing left to do.
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_lifetime, lifetime)) _lifetime = null;
            }

            lifetime.Dispose();
        }
    }

    /// <summary>
    /// A scheduled tick that fails must not kill the poller: the next interval
    /// gets another chance. Cancellation still unwinds the loop.
    /// </summary>
    private async Task TickAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteAsync(SensorPollReason.Scheduled, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Transient: the source reports its own unavailability.
        }
    }

    private async Task ExecuteAsync(SensorPollReason reason, CancellationToken cancellationToken)
    {
        await _single.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _tick(reason, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _single.Release();
        }
    }
}
