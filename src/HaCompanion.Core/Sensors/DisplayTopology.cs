namespace HaCompanion.Core.Sensors;

/// <summary>How a display is attached, where Windows reports it reliably.</summary>
public enum DisplayConnection
{
    /// <summary>Windows did not classify the output; nothing is claimed either way.</summary>
    Unknown,

    /// <summary>A built-in panel (laptop screen, all-in-one).</summary>
    Internal,

    /// <summary>An attached monitor, projector or wireless display.</summary>
    External
}

/// <summary>
/// One active display, described only by its mode. Deliberately carries no
/// manufacturer name, EDID serial, device path or monitor id: those are stable
/// hardware identifiers and are never collected.
/// </summary>
public sealed record DisplayInfo(
    int Width,
    int Height,
    int RefreshRateHz,
    int ScalePercent,
    DisplayConnection Connection,
    bool IsPrimary)
{
    /// <summary>A display reporting no pixels is a stale or detached entry.</summary>
    public bool IsUsable => Width > 0 && Height > 0;

    public string Resolution => $"{Width}x{Height}";
}

/// <summary>
/// Deterministic formatting and ordering for the display sensors. Pure, so the
/// selection rules can be tested without a monitor attached.
/// </summary>
public static class DisplaySummary
{
    public const string NoDisplays = "No Displays";

    /// <summary>Keeps the state well under Home Assistant's 255-character limit.</summary>
    public const int MaxListed = 4;

    /// <summary>Attributes stay bounded no matter how many outputs a dock exposes.</summary>
    public const int MaxDetailed = 8;

    /// <summary>
    /// Primary display first, then largest first, so the state text is stable
    /// across polls rather than following whatever order the OS enumerated in.
    /// </summary>
    public static IReadOnlyList<DisplayInfo> Order(IEnumerable<DisplayInfo?> displays) =>
        displays
            .Where(display => display is { IsUsable: true })
            .Select(display => display!)
            .OrderByDescending(display => display.IsPrimary)
            .ThenByDescending(display => (long)display.Width * display.Height)
            .ThenByDescending(display => display.Width)
            .ThenByDescending(display => display.RefreshRateHz)
            .ThenBy(display => display.Connection)
            .ToList();

    public static int Count(IEnumerable<DisplayInfo?> displays) => Order(displays).Count;

    /// <summary>
    /// A one-line summary of the active resolutions, e.g. "3840x2160 + 1920x1200".
    /// Bounded so a docking station with many outputs cannot produce an
    /// oversized state.
    /// </summary>
    public static string Describe(IEnumerable<DisplayInfo?> displays)
    {
        var ordered = Order(displays);
        if (ordered.Count == 0) return NoDisplays;

        var text = string.Join(" + ", ordered.Take(MaxListed).Select(display => display.Resolution));
        var remaining = ordered.Count - Math.Min(ordered.Count, MaxListed);

        return remaining > 0 ? $"{text} + {remaining} more" : text;
    }

    /// <summary>Full detail for one display, e.g. "3840x2160 @ 60 Hz, 150%, built-in".</summary>
    public static string DescribeDetail(DisplayInfo display)
    {
        var parts = new List<string> { display.Resolution };

        if (display.RefreshRateHz > 0) parts[0] += $" @ {display.RefreshRateHz} Hz";
        if (display.ScalePercent > 0) parts.Add($"{display.ScalePercent}%");

        var connection = display.Connection switch
        {
            DisplayConnection.Internal => "built-in",
            DisplayConnection.External => "external",
            _ => null
        };
        if (connection is not null) parts.Add(connection);

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Bounded per-display detail for the resolution sensor's attributes. Useful
    /// for templates without turning the entity into a hardware inventory.
    /// </summary>
    public static IDictionary<string, object> BuildAttributes(IEnumerable<DisplayInfo?> displays)
    {
        var ordered = Order(displays);
        var attributes = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["count"] = ordered.Count,
            ["built_in"] = ordered.Count(d => d.Connection == DisplayConnection.Internal),
            ["external"] = ordered.Count(d => d.Connection == DisplayConnection.External),
            ["displays"] = ordered.Take(MaxDetailed).Select(DescribeDetail).ToArray()
        };

        var primary = ordered.FirstOrDefault();
        if (primary is not null)
        {
            attributes["primary"] = DescribeDetail(primary);
            attributes["primary_resolution"] = primary.Resolution;
            if (primary.RefreshRateHz > 0) attributes["primary_refresh_rate"] = primary.RefreshRateHz;
            if (primary.ScalePercent > 0) attributes["primary_scale"] = primary.ScalePercent;
        }

        return attributes;
    }

    public static string IconFor(int count) => count switch
    {
        <= 0 => "mdi:monitor-off",
        1 => "mdi:monitor",
        _ => "mdi:monitor-multiple"
    };
}
