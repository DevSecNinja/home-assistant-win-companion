namespace WindowsCompanion.Core.Sensors;

public enum MonitorCaptureStatus
{
    Available,
    Unavailable
}

/// <summary>
/// Public monitor identity plus an internal-only key used to distinguish and
/// deduplicate physical targets.
/// </summary>
public sealed record MonitorIdentity
{
    public const int MaxModelLength = 160;

    public MonitorIdentity(
        string internalKey,
        string? model,
        string? manufacturer,
        string? productCode,
        DisplayConnection connection,
        bool isPrimary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(internalKey);

        InternalKey = internalKey.Trim();
        Model = Normalize(model, MaxModelLength);
        Manufacturer = Normalize(manufacturer, 3)?.ToUpperInvariant();
        ProductCode = Normalize(productCode, 4)?.ToUpperInvariant();
        Connection = connection;
        IsPrimary = isPrimary;
    }

    public string InternalKey { get; }
    public string? Model { get; }
    public string? Manufacturer { get; }
    public string? ProductCode { get; }
    public DisplayConnection Connection { get; }
    public bool IsPrimary { get; }

    public static MonitorIdentity Create(
        string internalKey,
        string? model,
        bool modelIsTrusted,
        ushort edidManufacturerId,
        ushort edidProductCode,
        bool edidIdsValid,
        DisplayConnection connection,
        bool isPrimary)
    {
        var normalizedModel = modelIsTrusted ? Normalize(model, MaxModelLength) : null;
        var manufacturer = edidIdsValid ? DecodeManufacturer(edidManufacturerId) : null;
        var productCode = edidIdsValid && edidProductCode != 0
            ? edidProductCode.ToString("X4")
            : null;

        return new MonitorIdentity(
            internalKey,
            normalizedModel,
            manufacturer,
            productCode,
            connection,
            isPrimary);
    }

    /// <summary>
    /// CCD exposes the two big-endian EDID manufacturer bytes as a little-endian
    /// ushort, so swap them before decoding the three five-bit EISA letters.
    /// </summary>
    public static string? DecodeManufacturer(ushort displayConfigValue)
    {
        var value = (ushort)((displayConfigValue >> 8) | (displayConfigValue << 8));
        Span<char> letters =
        [
            DecodeLetter((value >> 10) & 0x1f),
            DecodeLetter((value >> 5) & 0x1f),
            DecodeLetter(value & 0x1f)
        ];

        return letters.Contains('\0') ? null : new string(letters);
    }

    private static char DecodeLetter(int value) =>
        value is >= 1 and <= 26 ? (char)('A' + value - 1) : '\0';

    private static string? Normalize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var result = new System.Text.StringBuilder(Math.Min(value.Length, maxLength));
        var pendingSpace = false;

        foreach (var character in value)
        {
            if (char.IsControl(character) || char.IsWhiteSpace(character))
            {
                pendingSpace = result.Length > 0;
                continue;
            }

            if (pendingSpace && result.Length < maxLength - 1)
                result.Append(' ');

            if (result.Length >= maxLength) break;
            result.Append(character);
            pendingSpace = false;
        }

        return result.Length == 0 ? null : result.ToString();
    }
}

public sealed record MonitorCaptureResult(
    MonitorCaptureStatus Status,
    IReadOnlyList<MonitorIdentity> Monitors)
{
    public static MonitorCaptureResult Available(IEnumerable<MonitorIdentity?> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        return new(
            MonitorCaptureStatus.Available,
            monitors.Where(monitor => monitor is not null).Select(monitor => monitor!).ToArray());
    }

    public static MonitorCaptureResult Unavailable { get; } =
        new(MonitorCaptureStatus.Unavailable, []);
}

public static class MonitorIdentitySummary
{
    public const string NoMonitors = "No monitors";
    public const string Unavailable = "Unavailable";
    public const int MaxDetailed = 8;

    public static IReadOnlyList<MonitorIdentity> Order(IEnumerable<MonitorIdentity?> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        return monitors
            .Where(monitor => monitor is not null)
            .Select(monitor => monitor!)
            .GroupBy(monitor => monitor.InternalKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(monitor => monitor.IsPrimary)
                .ThenByDescending(Completeness)
                .ThenBy(monitor => monitor.Model, StringComparer.Ordinal)
                .ThenBy(monitor => monitor.Manufacturer, StringComparer.Ordinal)
                .ThenBy(monitor => monitor.ProductCode, StringComparer.Ordinal)
                .First())
            .OrderByDescending(monitor => monitor.IsPrimary)
            .ThenBy(ConnectionOrder)
            .ThenBy(monitor => monitor.Model, StringComparer.Ordinal)
            .ThenBy(monitor => monitor.Manufacturer, StringComparer.Ordinal)
            .ThenBy(monitor => monitor.ProductCode, StringComparer.Ordinal)
            .ThenBy(monitor => monitor.InternalKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string Describe(MonitorCaptureResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status == MonitorCaptureStatus.Unavailable) return Unavailable;

        var ordered = Order(result.Monitors);
        return ordered.Count switch
        {
            0 => NoMonitors,
            1 => DisplayLabel(ordered[0]),
            _ => $"{ordered.Count} monitors"
        };
    }

    public static IDictionary<string, object>? BuildAttributes(MonitorCaptureResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status == MonitorCaptureStatus.Unavailable) return null;

        var ordered = Order(result.Monitors);
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["count"] = ordered.Count,
            ["monitors"] = ordered
                .Take(MaxDetailed)
                .Select(BuildMonitorAttributes)
                .ToArray()
        };
    }

    public static string Signature(MonitorCaptureResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status == MonitorCaptureStatus.Unavailable) return Unavailable;

        return string.Join(
            '\u001e',
            Order(result.Monitors).Select(monitor => string.Join(
                '\u001f',
                monitor.Model ?? string.Empty,
                monitor.Manufacturer ?? string.Empty,
                monitor.ProductCode ?? string.Empty,
                ConnectionName(monitor.Connection),
                monitor.IsPrimary ? "1" : "0")));
    }

    private static Dictionary<string, object> BuildMonitorAttributes(MonitorIdentity monitor)
    {
        var attributes = new Dictionary<string, object>(StringComparer.Ordinal);
        if (monitor.Model is not null) attributes["model"] = monitor.Model;
        if (monitor.Manufacturer is not null) attributes["manufacturer"] = monitor.Manufacturer;
        if (monitor.ProductCode is not null) attributes["product_code"] = monitor.ProductCode;
        attributes["connection"] = ConnectionName(monitor.Connection);
        attributes["primary"] = monitor.IsPrimary;
        return attributes;
    }

    private static string DisplayLabel(MonitorIdentity monitor)
    {
        if (monitor.Model is not null) return monitor.Model;

        var label = string.Join(
            ' ',
            new[] { monitor.Manufacturer, monitor.ProductCode }
                .Where(value => value is not null));
        return label.Length > 0 ? label : "Unknown monitor";
    }

    private static int Completeness(MonitorIdentity monitor) =>
        (monitor.Model is null ? 0 : 1)
        + (monitor.Manufacturer is null ? 0 : 1)
        + (monitor.ProductCode is null ? 0 : 1);

    private static int ConnectionOrder(MonitorIdentity monitor) => monitor.Connection switch
    {
        DisplayConnection.Internal => 0,
        DisplayConnection.External => 1,
        _ => 2
    };

    private static string ConnectionName(DisplayConnection connection) => connection switch
    {
        DisplayConnection.Internal => "built_in",
        DisplayConnection.External => "external",
        _ => "unknown"
    };
}
