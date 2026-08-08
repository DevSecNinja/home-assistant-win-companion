using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HaCompanion.Core.App;

/// <summary>Why the connection is being brought up, taken down or rebuilt.</summary>
public enum LifecycleIntent
{
    /// <summary>The user, or startup, is bringing the connection up.</summary>
    Start,

    /// <summary>The user is taking the connection down but keeping the server.</summary>
    Stop,

    /// <summary>The user is removing the server and all of its local state.</summary>
    Forget,

    /// <summary>
    /// Settings changed. Whether the connection is wanted is unaffected, so
    /// saving settings while disconnected does not reconnect.
    /// </summary>
    Reconfigure,

    /// <summary>Background routing wants to move the connection to another address.</summary>
    RouteSwitch
}

/// <summary>
/// Exclusive right to change the connection, held for the whole of one lifecycle
/// transition and released by disposing.
/// </summary>
public sealed class LifecycleLease : IDisposable
{
    private readonly ConnectionLifecycle _owner;
    private readonly CancellationTokenSource _cancellation;
    private int _disposed;

    internal LifecycleLease(
        ConnectionLifecycle owner, LifecycleIntent intent, long epoch, CancellationTokenSource cancellation)
    {
        _owner = owner;
        Intent = intent;
        Epoch = epoch;
        _cancellation = cancellation;
    }

    public LifecycleIntent Intent { get; }

    /// <summary>The lifecycle generation this lease was granted in.</summary>
    public long Epoch { get; }

    /// <summary>
    /// Cancelled when the user asks for something that supersedes this work, so a
    /// long rebuild does not make an explicit disconnect wait for the network.
    /// </summary>
    public CancellationToken Token => _cancellation.Token;

    /// <summary>False once a newer intent has superseded this one.</summary>
    public bool IsCurrent => _owner.Epoch == Epoch && !_cancellation.IsCancellationRequested;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _owner.Release(this);
    }
}

/// <summary>
/// Serializes every change to the connection - sign-in, resume, disconnect,
/// remove-server, settings changes and background route switches - so two of them
/// can never interleave and leave a second live connection behind, and so a
/// background route switch can never resurrect a connection the user just ended.
/// </summary>
/// <remarks>
/// Two mechanisms, because ordering alone is not enough. The gate orders the
/// transitions; the generation counter lets a background route switch notice that
/// the user has since asked for something else and stand down rather than finish
/// work nobody wants. An explicit intent also pre-empts whatever is running, so
/// the UI never waits on a rebuild's network calls.
/// </remarks>
public sealed class ConnectionLifecycle : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _sync = new();
    private readonly ILogger<ConnectionLifecycle> _log;

    private long _epoch;
    private bool _connectionWanted;
    private LifecycleLease? _held;
    private CancellationTokenSource? _inFlight;

    public ConnectionLifecycle(ILogger<ConnectionLifecycle>? log = null)
        => _log = log ?? NullLogger<ConnectionLifecycle>.Instance;

    /// <summary>Bumped by every explicit intent; stale background work compares against it.</summary>
    public long Epoch
    {
        get { lock (_sync) return _epoch; }
    }

    /// <summary>
    /// Whether a connection is currently wanted at all. False after an explicit
    /// disconnect or remove-server, which is what stops a queued route switch from
    /// bringing the connection back.
    /// </summary>
    public bool ConnectionWanted
    {
        get { lock (_sync) return _connectionWanted; }
    }

    /// <summary>What is holding the lifecycle right now, for diagnostics and tests.</summary>
    public LifecycleIntent? CurrentIntent
    {
        get { lock (_sync) return _held?.Intent; }
    }

    /// <summary>
    /// Takes the lifecycle for a user-initiated change, waiting for any transition
    /// already in progress and pre-empting it first.
    /// </summary>
    public async Task<LifecycleLease> AcquireAsync(
        LifecycleIntent intent, CancellationToken ct = default)
    {
        if (intent == LifecycleIntent.RouteSwitch)
        {
            throw new ArgumentException(
                "A route switch must be taken with TryAcquireRouteSwitchAsync so it can stand down.",
                nameof(intent));
        }

        Preempt();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        return Grant(intent, ct);
    }

    /// <summary>
    /// Takes the lifecycle for a background route switch, or returns null when the
    /// switch is no longer wanted.
    /// </summary>
    /// <remarks>
    /// Never queues: if a transition is already running, this switch is simply
    /// dropped. Whatever prompted it - a failed sync, a network change - will
    /// prompt it again, and the transition in progress may well have settled it.
    /// </remarks>
    public async Task<LifecycleLease?> TryAcquireRouteSwitchAsync(CancellationToken ct = default)
    {
        long observed;
        lock (_sync)
        {
            if (!_connectionWanted) return null;
            observed = _epoch;
        }

        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false)) return null;

        // Re-checked under the gate: an explicit intent may have completed in the
        // window between reading the generation and being let in.
        lock (_sync)
        {
            if (!_connectionWanted || _epoch != observed)
            {
                _gate.Release();
                _log.LogDebug("Route switch stood down; the connection was changed meanwhile.");
                return null;
            }

            return GrantLocked(LifecycleIntent.RouteSwitch, ct);
        }
    }

    private LifecycleLease Grant(LifecycleIntent intent, CancellationToken ct)
    {
        lock (_sync) return GrantLocked(intent, ct);
    }

    private LifecycleLease GrantLocked(LifecycleIntent intent, CancellationToken ct)
    {
        if (intent != LifecycleIntent.RouteSwitch)
        {
            _epoch++;
            if (intent is LifecycleIntent.Start) _connectionWanted = true;
            else if (intent is LifecycleIntent.Stop or LifecycleIntent.Forget) _connectionWanted = false;
        }

        _inFlight = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var lease = new LifecycleLease(this, intent, _epoch, _inFlight);
        _held = lease;
        return lease;
    }

    internal void Release(LifecycleLease lease)
    {
        lock (_sync)
        {
            // A lease that is no longer the held one has already been released.
            if (!ReferenceEquals(_held, lease)) return;
            _held = null;
            _inFlight?.Dispose();
            _inFlight = null;
        }

        _gate.Release();
    }

    /// <summary>
    /// Cancels the transition in progress, if any, so a user action does not wait
    /// on a background rebuild's network calls.
    /// </summary>
    private void Preempt()
    {
        CancellationTokenSource? inFlight;
        lock (_sync) inFlight = _inFlight;

        if (inFlight is null) return;

        try
        {
            inFlight.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The transition finished on its own between the read and the cancel.
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _inFlight?.Dispose();
            _inFlight = null;
            _held = null;
        }

        _gate.Dispose();
    }
}
