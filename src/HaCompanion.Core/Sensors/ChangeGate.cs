namespace HaCompanion.Core.Sensors;

/// <summary>
/// Holds the value a source last published and decides whether a new reading is
/// worth pushing.
/// </summary>
/// <remarks>
/// Sensor sources exist to produce traffic only when something actually changes:
/// a poll that sees the same value must not cost a webhook call or a Home
/// Assistant recorder row. Every source was writing the same lock-compare-store
/// dance to achieve that, so it lives here once, is thread-safe, and is unit
/// tested. Sources that need more than equality - disk space, where only a
/// meaningful movement counts - supply their own predicate.
/// </remarks>
public sealed class ChangeGate<T>
{
    private readonly Func<T, T, bool> _hasChanged;
    private readonly object _gate = new();
    private T _current;

    public ChangeGate(T initial, Func<T, T, bool>? hasChanged = null)
    {
        _current = initial;
        _hasChanged = hasChanged
                      ?? ((previous, current) => !EqualityComparer<T>.Default.Equals(previous, current));
    }

    /// <summary>The value most recently accepted by the gate.</summary>
    public T Current
    {
        get { lock (_gate) return _current; }
    }

    /// <summary>
    /// Stores <paramref name="value"/> and reports whether it counts as a change.
    /// A reading that does not count is discarded, so the published value stays
    /// stable instead of drifting.
    /// </summary>
    public bool TryUpdate(T value)
    {
        lock (_gate)
        {
            if (!_hasChanged(_current, value)) return false;
            _current = value;
            return true;
        }
    }

    /// <summary>
    /// Records a value without reporting a change. Used when a source starts
    /// observing: the first reading is the baseline, not news.
    /// </summary>
    public void Seed(T value)
    {
        lock (_gate) _current = value;
    }
}
