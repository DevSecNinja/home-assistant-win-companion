namespace WindowsCompanion.Core.Sensors;

/// <summary>
/// Limits display collection to a monitor count until the user explicitly opts
/// into mode, scaling and connection details.
/// </summary>
public static class DisplayCapturePolicy
{
    public const string DisplayCountId = "displays_count";
    public const string DisplayResolutionId = "display_resolution";
    public const string MonitorIdentityId = "monitor_identity";

    public static DisplayCaptureScope For(IReadOnlySet<string> enabled) =>
        enabled.Contains(MonitorIdentityId)
            ? DisplayCaptureScope.Identity
            : enabled.Contains(DisplayResolutionId)
            ? DisplayCaptureScope.Details
            : enabled.Contains(DisplayCountId)
                ? DisplayCaptureScope.CountOnly
                : DisplayCaptureScope.None;
}

/// <summary>
/// Captures and compares only the display scope selected by
/// <see cref="DisplayCapturePolicy"/>.
/// </summary>
public sealed class DisplayObservationGate
{
    private readonly Func<int> _captureCount;
    private readonly Func<IReadOnlyList<DisplayInfo>> _captureDetails;
    private readonly ChangeGate<int> _count = new(0);
    private readonly ChangeGate<string> _summary = new(string.Empty);

    public DisplayObservationGate(
        Func<int> captureCount,
        Func<IReadOnlyList<DisplayInfo>> captureDetails)
    {
        _captureCount = captureCount ?? throw new ArgumentNullException(nameof(captureCount));
        _captureDetails = captureDetails ?? throw new ArgumentNullException(nameof(captureDetails));
    }

    public int CaptureCount()
    {
        var count = _captureCount();
        _count.Seed(count);
        return count;
    }

    public IReadOnlyList<DisplayInfo> CaptureDetails()
    {
        var displays = _captureDetails();
        SeedDetails(displays);
        return displays;
    }

    public void SeedCount(int count) => _count.Seed(count);

    public void SeedDetails(IReadOnlyList<DisplayInfo> displays) =>
        _summary.Seed(DisplaySummary.Describe(displays));

    public bool TryUpdateCount(int count) => _count.TryUpdate(count);

    public bool TryUpdateDetails(IReadOnlyList<DisplayInfo> displays) =>
        _summary.TryUpdate(DisplaySummary.Describe(displays));

    public void Seed(DisplayCaptureScope scope)
    {
        if (scope == DisplayCaptureScope.Details)
            CaptureDetails();
        else if (scope == DisplayCaptureScope.CountOnly)
            CaptureCount();
    }

    public bool TryUpdate(DisplayCaptureScope scope) => scope switch
    {
        DisplayCaptureScope.Details => TryUpdateDetails(_captureDetails()),
        DisplayCaptureScope.CountOnly => _count.TryUpdate(_captureCount()),
        _ => false
    };
}

public enum DisplayCaptureScope
{
    None,
    CountOnly,
    Details,
    Identity
}
