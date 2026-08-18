namespace WindowsCompanion_App.Services;

internal static class SensorPreviewPresentation
{
    public static bool IsActive(
        bool sensorsSelected,
        bool windowVisible,
        bool minimized,
        bool exiting) =>
        sensorsSelected && windowVisible && !minimized && !exiting;
}

internal sealed class SensorPreviewCancellation
{
    private readonly object _gate = new();
    private readonly Dictionary<string, CancellationTokenSource> _rowPreviews =
        new(StringComparer.Ordinal);
    private CancellationTokenSource? _listPreview;

    public CancellationTokenSource BeginList()
    {
        var next = new CancellationTokenSource();
        CancellationTokenSource? previous;
        lock (_gate)
        {
            previous = _listPreview;
            _listPreview = next;
        }

        previous?.Cancel();
        return next;
    }

    public CancellationTokenSource? TryBeginList()
    {
        lock (_gate)
        {
            if (_listPreview is not null) return null;

            _listPreview = new CancellationTokenSource();
            return _listPreview;
        }
    }

    public void EndList(CancellationTokenSource completed)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_listPreview, completed))
                _listPreview = null;
        }
    }

    public CancellationTokenSource BeginRow(string uniqueId)
    {
        var next = new CancellationTokenSource();
        CancellationTokenSource? previous = null;
        lock (_gate)
        {
            _rowPreviews.Remove(uniqueId, out previous);
            _rowPreviews[uniqueId] = next;
        }

        previous?.Cancel();
        return next;
    }

    public void EndRow(string uniqueId, CancellationTokenSource completed)
    {
        lock (_gate)
        {
            if (_rowPreviews.TryGetValue(uniqueId, out var current)
                && ReferenceEquals(current, completed))
            {
                _rowPreviews.Remove(uniqueId);
            }
        }
    }

    public void CancelList()
    {
        CancellationTokenSource? listPreview;
        lock (_gate)
        {
            listPreview = _listPreview;
            _listPreview = null;
        }

        listPreview?.Cancel();
    }

    public void CancelAll()
    {
        CancellationTokenSource? listPreview;
        List<CancellationTokenSource> rowPreviews;
        lock (_gate)
        {
            listPreview = _listPreview;
            _listPreview = null;
            rowPreviews = [.. _rowPreviews.Values];
            _rowPreviews.Clear();
        }

        listPreview?.Cancel();
        foreach (var cancellation in rowPreviews)
            cancellation.Cancel();
    }
}
