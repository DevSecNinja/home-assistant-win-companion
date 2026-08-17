namespace WindowsCompanion.Core.Models;

/// <summary>
/// Payload for the Home Assistant <c>update_location</c> webhook command.
/// This updates the device tracker entity shown on the map, enabling
/// zone-based states (e.g. "Home") instead of raw coordinates.
/// </summary>
public sealed record LocationUpdate(
    double? Latitude,
    double? Longitude,
    int? GpsAccuracy,
    string? LocationName = null)
{
    /// <summary>True when a real fix is available.</summary>
    public bool HasFix => Latitude.HasValue && Longitude.HasValue;
}
