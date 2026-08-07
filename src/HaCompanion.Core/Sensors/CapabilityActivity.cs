namespace HaCompanion.Core.Sensors;

public static class CapabilityActivity
{
    public static bool IsActive(IEnumerable<long?> lastUsedStopValues)
    {
        ArgumentNullException.ThrowIfNull(lastUsedStopValues);
        return lastUsedStopValues.Any(value => value is <= 0);
    }
}
