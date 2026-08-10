namespace WindowsCompanion.Core.Sensors;

public enum WifiConnectionStatus
{
    Connected,
    NotConnected,
    PermissionRequired,
    Unavailable
}

/// <summary>
/// Maps the native <c>DOT11_AUTH_ALGORITHM</c> code Windows reports for the current
/// connection to the security label shown in Windows' own Wi-Fi UI, so the sensor
/// can flag a legacy or open network at a glance.
/// </summary>
public static class WifiSecurityClassifier
{
    public static string? Describe(int authAlgorithm) => authAlgorithm switch
    {
        1 => "Open",
        2 => "Shared Key (WEP)",
        3 => "WPA-Enterprise",
        4 => "WPA-Personal",
        5 => "WPA-None",
        6 => "WPA2-Enterprise",
        7 => "WPA2-Personal",
        8 => "WPA3-Enterprise (192-bit)",
        9 => "WPA3-Personal",
        10 => "Enhanced Open (OWE)",
        11 => "WPA3-Enterprise",
        _ => null
    };
}

public sealed record WifiConnectionInfo(
    WifiConnectionStatus Status,
    string? Ssid = null,
    byte[]? Bssid = null,
    int? AuthAlgorithm = null,
    bool? MacRandomizationEnabled = null,
    string? CurrentMacAddress = null)
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

    /// <summary>The Wi-Fi security type of the current connection, for example
    /// <c>WPA2-Personal</c>, so a legacy or open network stands out.</summary>
    public string SecurityState => Status switch
    {
        WifiConnectionStatus.Connected => AuthAlgorithm is { } algorithm
            ? WifiSecurityClassifier.Describe(algorithm) ?? "Unknown"
            : "Unavailable",
        WifiConnectionStatus.PermissionRequired => "Location permission required",
        WifiConnectionStatus.NotConnected => "Not Connected",
        _ => "Unavailable"
    };

    /// <summary>
    /// The randomized MAC address currently in use for this connection, when
    /// Windows' per-network privacy setting has randomization switched on.
    /// </summary>
    public string RandomMacAddressState => Status switch
    {
        WifiConnectionStatus.Connected => MacRandomizationEnabled switch
        {
            true => CurrentMacAddress ?? "Unavailable",
            false => "Not randomized",
            null => "Unavailable"
        },
        WifiConnectionStatus.PermissionRequired => "Location permission required",
        WifiConnectionStatus.NotConnected => "Not Connected",
        _ => "Unavailable"
    };
}
