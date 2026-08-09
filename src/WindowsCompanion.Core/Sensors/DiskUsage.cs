namespace WindowsCompanion.Core.Sensors;

/// <summary>
/// A point-in-time reading of one volume. Only totals are carried: no volume
/// label, file-system layout, path or device identifier.
/// </summary>
public readonly record struct DiskUsage(long TotalBytes, long FreeBytes)
{
    /// <summary>Used when the volume is missing, locked, or failed to answer.</summary>
    public static DiskUsage Unavailable => default;

    /// <summary>
    /// Guards against the nonsense a transient, BitLocker-locked or disconnected
    /// volume can report, so no sensor ever publishes a negative or absurd value.
    /// </summary>
    public bool IsAvailable => TotalBytes > 0 && FreeBytes >= 0 && FreeBytes <= TotalBytes;

    public long UsedBytes => IsAvailable ? TotalBytes - FreeBytes : 0;
}

/// <summary>
/// Rounds, formats and change-detects disk readings.
/// </summary>
/// <remarks>
/// Free space on a working PC drifts continuously. Reporting every wobble would
/// write a Home Assistant recorder row on every poll for no user benefit, so the
/// published value is only replaced once the change is large enough to mean
/// something. Everything here is pure and unit tested.
/// </remarks>
public static class DiskUsageFormatter
{
    /// <summary>Home Assistant's "GB" is decimal, matching how Windows advertises drives.</summary>
    public const double BytesPerGigabyte = 1_000_000_000d;

    /// <summary>Percentage-point movement that counts as a real change.</summary>
    public const double PercentThreshold = 0.5;

    /// <summary>Free-space movement, in GB, that counts as a real change.</summary>
    public const double GigabyteThreshold = 1.0;

    public static double ToGigabytes(long bytes) =>
        Math.Round(Math.Max(0, bytes) / BytesPerGigabyte, 1);

    public static double? FreeGigabytes(DiskUsage usage) =>
        usage.IsAvailable ? ToGigabytes(usage.FreeBytes) : null;

    public static double? UsedGigabytes(DiskUsage usage) =>
        usage.IsAvailable ? ToGigabytes(usage.UsedBytes) : null;

    public static double? TotalGigabytes(DiskUsage usage) =>
        usage.IsAvailable ? ToGigabytes(usage.TotalBytes) : null;

    public static double? UsedPercent(DiskUsage usage) =>
        usage.IsAvailable
            ? Math.Round(usage.UsedBytes * 100d / usage.TotalBytes, 1)
            : null;

    /// <summary>
    /// Whether a new reading is worth publishing. Appearing or disappearing always
    /// counts; otherwise the reading must move by
    /// <see cref="PercentThreshold"/> percentage points or
    /// <see cref="GigabyteThreshold"/> GB.
    /// </summary>
    public static bool HasMeaningfullyChanged(DiskUsage previous, DiskUsage current)
    {
        if (!current.IsAvailable) return previous.IsAvailable;
        if (!previous.IsAvailable) return true;

        var percent = Math.Abs(UsedPercent(current)!.Value - UsedPercent(previous)!.Value);
        var free = Math.Abs(FreeGigabytes(current)!.Value - FreeGigabytes(previous)!.Value);

        return percent >= PercentThreshold || free >= GigabyteThreshold;
    }

    public static string IconFor(double? usedPercent) => usedPercent switch
    {
        null => "mdi:harddisk",
        >= 90 => "mdi:gauge-full",
        >= 60 => "mdi:gauge",
        _ => "mdi:gauge-low"
    };
}
