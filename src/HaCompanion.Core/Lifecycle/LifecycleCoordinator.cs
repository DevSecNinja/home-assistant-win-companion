using HaCompanion.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HaCompanion.Core.Lifecycle;

/// <summary>
/// Owns what the companion does about a lifecycle transition: record it locally,
/// make one bounded attempt to tell Home Assistant, and report anything that was
/// never acknowledged once the machine is back.
/// </summary>
/// <remarks>
/// The local journal is the reliable mechanism and the final push is only an
/// optimisation. Windows gives an application a few seconds at most before it is
/// terminated, the network stack may already be gone, and a power cut gives nothing
/// at all - so a design that depends on the push arriving is a design that silently
/// loses transitions. Writing the record first and reporting it after the next
/// successful connection degrades to "late" instead of "never".
///
/// Nothing here blocks the caller. The push runs on a worker with a hard timeout,
/// because <see cref="Observe"/> is called from a window procedure that Windows is
/// waiting on: delaying it delays the shutdown itself.
/// </remarks>
public sealed class LifecycleCoordinator
{
    private readonly ILifecycleJournal _journal;
    private readonly Func<CancellationToken, Task<bool>>? _finalPush;
    private readonly TimeSpan _finalPushTimeout;
    private readonly IClock _clock;
    private readonly ILogger<LifecycleCoordinator> _log;
    private readonly object _gate = new();

    private CancellationTokenSource? _pushCts;
    private LifecycleRecord? _readRecord;

    /// <param name="finalPush">
    /// Pushes the current sensor states and reports whether Home Assistant accepted
    /// them. Null disables the optimisation, leaving journal-based recovery.
    /// </param>
    /// <param name="finalPushTimeout">
    /// Hard ceiling on that attempt. Kept short on purpose: past a couple of seconds
    /// the machine is usually gone anyway, and waiting only risks holding up exit.
    /// </param>
    public LifecycleCoordinator(
        ILifecycleJournal journal,
        Func<CancellationToken, Task<bool>>? finalPush = null,
        TimeSpan? finalPushTimeout = null,
        IClock? clock = null,
        ILogger<LifecycleCoordinator>? log = null)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _finalPush = finalPush;
        _finalPushTimeout = finalPushTimeout ?? TimeSpan.FromSeconds(2);
        _clock = clock ?? new SystemClock();
        _log = log ?? NullLogger<LifecycleCoordinator>.Instance;
    }

    public LifecycleTracker Tracker { get; } = new();

    /// <summary>
    /// The most recent transition Home Assistant is not known to have received, or
    /// null when everything observed has been acknowledged.
    /// </summary>
    public LifecycleRecord? Pending { get; private set; }

    /// <summary>
    /// Whether <see cref="Pending"/> describes the state the machine is in right
    /// now, as opposed to one it has already left - a suspend we woke from, or a
    /// shutdown from before this boot. Only the latter is worth reporting
    /// separately; repeating the current state would just say it twice.
    /// </summary>
    public bool PendingIsCurrent { get; private set; }

    /// <summary>Raised when the reported state changed and a push is warranted.</summary>
    public event Action? Changed;

    /// <summary>The in-flight final push, exposed so tests can await it.</summary>
    public Task? FinalPush { get; private set; }

    /// <summary>Loads the transition left behind by the previous run, if any.</summary>
    public void Start()
    {
        var record = _journal.Read();
        Pending = record is { Acknowledged: false } ? record : null;
        PendingIsCurrent = false;

        if (Pending is not null)
        {
            _log.LogInformation(
                "Recovered unreported lifecycle transition {Transition} observed at {ObservedAt}.",
                Pending.Transition, Pending.ObservedAt);
        }
    }

    public void Stop() => CancelPendingPush();

    /// <summary>Records one observation. Returns immediately; never throws.</summary>
    public void Observe(LifecycleSignal signal)
    {
        LifecycleObservation observation;
        lock (_gate)
        {
            observation = Tracker.Observe(signal, _clock.UtcNow);
            if (!observation.Changed) return;

            // A resume invalidates any suspend push still waiting to time out: it
            // would otherwise report a state the machine has already left.
            CancelPendingPush();

            if (observation.RequiresFinalPush)
            {
                Pending = new LifecycleRecord
                {
                    Transition = signal.Transition,
                    Reason = signal.Reason,
                    Critical = signal.Critical,
                    ObservedAt = Tracker.ChangedAt ?? _clock.UtcNow,
                    Acknowledged = false
                };

                PendingIsCurrent = true;
                _journal.Write(Pending);
            }
            else
            {
                // We are running again, so whatever is still pending is history.
                PendingIsCurrent = false;
            }
        }

        _log.LogInformation("Lifecycle transition {Transition} ({Reason}).", signal.Transition, signal.Reason);
        Changed?.Invoke();

        if (observation.RequiresFinalPush) BeginFinalPush();
    }

    /// <summary>
    /// Notes that the pending transition has just been read into an outgoing batch.
    /// Acknowledgement is tied to this snapshot so a sync that was already in flight
    /// cannot be mistaken for delivery of a transition observed after it started.
    /// </summary>
    public void NoteRead() => _readRecord = Pending;

    /// <summary>Called when Home Assistant accepted a batch that we read into.</summary>
    public void ReportDelivered()
    {
        lock (_gate)
        {
            var read = _readRecord;
            if (read is null || !ReferenceEquals(read, Pending) || read.Acknowledged) return;

            Pending = read with { Acknowledged = true };
            _journal.Write(Pending);
        }

        _log.LogDebug("Lifecycle transition acknowledged by Home Assistant.");
    }

    private void BeginFinalPush()
    {
        if (_finalPush is null) return;

        var cts = new CancellationTokenSource(_finalPushTimeout);
        _pushCts = cts;

        FinalPush = Task.Run(async () =>
        {
            try
            {
                var delivered = await _finalPush(cts.Token).ConfigureAwait(false);
                if (!delivered)
                    _log.LogWarning("Final lifecycle push was not acknowledged; it will be reported after the next start.");
            }
            catch (Exception ex)
            {
                // Best effort by definition: the machine is going away.
                _log.LogWarning(ex, "Final lifecycle push failed.");
            }
            finally
            {
                cts.Dispose();
            }
        });
    }

    private void CancelPendingPush()
    {
        var cts = _pushCts;
        _pushCts = null;
        if (cts is null) return;

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The push already finished and disposed its own source.
        }
    }
}
