namespace HaCompanion.Core.Sensors;

public enum WifiConnectionStatus
{
    Connected,
    NotConnected,
    PermissionRequired,
    Unavailable
}

public sealed record WifiConnectionInfo(
    WifiConnectionStatus Status,
    string? Ssid = null,
    byte[]? Bssid = null)
{
    public string SsidState => Status switch
    {
        WifiConnectionStatus.Connected => Ssid ?? "Not Connected",
        WifiConnectionStatus.PermissionRequired => "Location permission required",
        WifiConnectionStatus.NotConnected => "Not Connected",
        _ => "Unavailable"
    };

    public string BssidState => Status switch
    {
        WifiConnectionStatus.Connected when Bssid is { Length: 6 } =>
            string.Join(":", Bssid.Select(value => value.ToString("X2"))),
        WifiConnectionStatus.PermissionRequired => "Location permission required",
        WifiConnectionStatus.NotConnected => "Not Connected",
        _ => "Unavailable"
    };
}
