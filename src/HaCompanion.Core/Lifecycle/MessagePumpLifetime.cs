namespace HaCompanion.Core.Lifecycle;

/// <summary>
/// The start/stop handshake between a thread that owns a Windows message pump and
/// the callers that switch it on and off.
/// </summary>
/// <remarks>
/// A pump can only be shut down politely once it has a window to post to, but a
/// caller may well stop it before it ever gets that far - at sign-out, moments after
/// startup, or in a test. Without an agreed protocol that case leaks the pump: the
/// stopper sees no window, skips the close message, gives up on the join, and the
/// thread then creates its window and loops forever with nobody left holding a
/// handle to it.
///
/// The protocol closes that window of time from both sides:
///
/// * A stop request is recorded even when nothing is running yet, so the pump can
///   see it before or immediately after creating its window and leave on its own.
/// * The pump always reports itself ready - whether it succeeded, failed or never
///   started - so a stopper is never left waiting on an event that will not be set.
///
/// Because the flag is set before the stopper waits, and readiness is signalled
/// after the window handle is published, one of the two always happens: either the
/// pump sees the stop request, or the stopper sees a window it can close.
///
/// This type is deliberately free of any Windows dependency so the protocol itself
/// can be tested; the P/Invoke lives with the caller.
/// </remarks>
public sealed class MessagePumpLifetime : IDisposable
{
    private readonly object _gate = new();

    // Set when the pump has published its window handle, or has decided it never
    // will. Either way there is nothing further for a stopper to wait for.
    private readonly ManualResetEventSlim _ready = new(false);

    private bool _running;
    private bool _stopRequested;
    private bool _disposed;

    /// <summary>Whether a pump is currently owned by this lifetime.</summary>
    public bool IsRunning
    {
        get { lock (_gate) return _running; }
    }

    /// <summary>
    /// Whether the pump should leave at the first opportunity. Checked by the pump
    /// before and after it creates its window, which is what makes an early stop
    /// deterministic rather than a race with window creation.
    /// </summary>
    public bool StopRequested
    {
        get { lock (_gate) return _stopRequested; }
    }

    /// <summary>
    /// Claims ownership for one pump. Returns false when one is already running, so
    /// a repeated start is a no-op instead of a second orphaned thread.
    /// </summary>
    public bool TryBeginStart()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_running) return false;

            _running = true;
            _stopRequested = false;
            _ready.Reset();
            return true;
        }
    }

    /// <summary>
    /// Announces that the pump has published everything a stopper needs, or that it
    /// has none to publish. Safe to call more than once.
    /// </summary>
    public void MarkReady()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _ready.Set();
        }
    }

    /// <summary>
    /// Waits for that announcement. Returns false on timeout, which means the pump
    /// is wedged somewhere unexpected and the caller should stop waiting on it.
    /// </summary>
    public bool WaitUntilReady(TimeSpan timeout)
    {
        ManualResetEventSlim ready;
        lock (_gate)
        {
            if (_disposed) return false;
            ready = _ready;
        }

        try
        {
            return ready.Wait(timeout);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Asks the pump to leave. Returns false when nothing is running, so a duplicate
    /// stop does no work. The request is recorded even if the pump has not reached
    /// its first check yet - that is the whole point.
    /// </summary>
    public bool RequestStop()
    {
        lock (_gate)
        {
            if (!_running) return false;

            _stopRequested = true;
            return true;
        }
    }

    /// <summary>
    /// Records that no pump is running any more and releases anyone still waiting
    /// for readiness. Called by the pump as it leaves, and by a stopper that has
    /// established the thread is gone. Idempotent, and leaves the lifetime reusable.
    /// </summary>
    public void MarkStopped()
    {
        lock (_gate)
        {
            _running = false;
            _stopRequested = false;

            // A stopper may be waiting on readiness that a pump which never started
            // will never announce. Unblock it rather than let it time out.
            if (!_disposed) _ready.Set();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;
            _running = false;
            _stopRequested = false;
            _ready.Set();
            _ready.Dispose();
        }
    }
}
