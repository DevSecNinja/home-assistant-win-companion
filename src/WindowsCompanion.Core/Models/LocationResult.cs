namespace WindowsCompanion.Core.Models;

public enum LocationStatus
{
    /// <summary>A usable position was obtained.</summary>
    Ready,

    /// <summary>
    /// Windows Location Services are off, or this app has not been granted
    /// location permission.
    /// </summary>
    PermissionDenied,

    /// <summary>
    /// Access is allowed but no position could be produced right now (no data
    /// source, timeout, or an unexpected provider failure).
    /// </summary>
    Unavailable
}

/// <summary>One reading from an <see cref="Abstractions.ILocationProvider"/>.</summary>
public sealed record LocationResult(
    LocationStatus Status,
    double? Latitude = null,
    double? Longitude = null,
    double? AccuracyMeters = null,
    DateTimeOffset? Timestamp = null)
{
    /// <summary>A no-data result with all coordinate fields null.</summary>
    public static LocationResult Unavailable(LocationStatus status = LocationStatus.Unavailable)
    {
        if (status == LocationStatus.Ready)
            throw new ArgumentException(
                "Ready is not a valid status for an unavailable result.", nameof(status));

        return new LocationResult(status);
    }

    /// <summary>A usable position, capturing "now" as the reading's timestamp.</summary>
    public static LocationResult Ready(double latitude, double longitude, double accuracyMeters) =>
        new(LocationStatus.Ready, latitude, longitude, accuracyMeters, DateTimeOffset.UtcNow);
}
