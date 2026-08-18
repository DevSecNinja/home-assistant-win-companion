namespace WindowsCompanion.Core.Sensors;

public enum WireGuardStatus
{
    Unavailable,
    Disconnected,
    Connected
}

public static class WireGuardStatusFormatter
{
    public static string Format(WireGuardStatus status) => status switch
    {
        WireGuardStatus.Connected => "connected",
        WireGuardStatus.Disconnected => "disconnected",
        _ => "unavailable"
    };
}

public static class WireGuardStatusClassifier
{
    public static WireGuardStatus Classify(
        bool inspectionSucceeded,
        bool clientDetected,
        IReadOnlySet<string> runningTunnels,
        IReadOnlySet<string> operationalAdapters)
    {
        if (!inspectionSucceeded || !clientDetected)
            return WireGuardStatus.Unavailable;

        foreach (var tunnel in runningTunnels)
        {
            if (operationalAdapters.Contains(tunnel))
                return WireGuardStatus.Connected;
        }

        return WireGuardStatus.Disconnected;
    }
}
