namespace WindowsCompanion.Core.Sensors;

public readonly record struct FrontmostAppSnapshot(
    string? ApplicationName,
    string? WindowTitle);

public static class FrontmostAppState
{
    public const int MaxStateLength = 255;

    public static string Select(
        FrontmostAppSnapshot snapshot,
        FrontmostAppMode mode)
    {
        var value = mode == FrontmostAppMode.FullWindowTitle
            ? snapshot.WindowTitle
            : snapshot.ApplicationName;

        if (string.IsNullOrWhiteSpace(value))
            value = snapshot.ApplicationName;
        if (string.IsNullOrWhiteSpace(value))
            return "Unavailable";

        return value.Length <= MaxStateLength
            ? value
            : value[..MaxStateLength];
    }
}

public sealed class DebouncedValue<T>
{
    private readonly object _gate = new();
    private T? _current;
    private T? _pending;
    private long _version;
    private bool _hasCurrent;

    public T? Current
    {
        get
        {
            lock (_gate) return _current;
        }
    }

    public void SetInitial(T value)
    {
        lock (_gate)
        {
            _current = value;
            _pending = value;
            _hasCurrent = true;
            _version++;
        }
    }

    public void InvalidatePending()
    {
        lock (_gate)
        {
            _pending = _current;
            _version++;
        }
    }

    public bool TryGetCurrent(out T? value)
    {
        lock (_gate)
        {
            value = _current;
            return _hasCurrent;
        }
    }

    public long Stage(T value)
    {
        lock (_gate)
        {
            _pending = value;
            return ++_version;
        }
    }

    public bool TryCommit(long version)
    {
        lock (_gate)
        {
            if (version != _version || EqualityComparer<T>.Default.Equals(_current, _pending))
                return false;

            _current = _pending;
            _hasCurrent = true;
            return true;
        }
    }
}
