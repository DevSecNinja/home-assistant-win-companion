namespace HaCompanion.Core.Sensors;

/// <summary>
/// Decides what a local preview is allowed to collect. A privacy-sensitive value is
/// never gathered - not even to show the user locally - until that specific sensor
/// has been switched on, so opening the settings page cannot turn into collection by
/// itself and enabling one identifier never reveals a neighbouring one.
/// </summary>
public static class SensorPreviewGate
{
    public static IReadOnlySet<string> Permitted(
        IEnumerable<SensorDefinition> definitions,
        IReadOnlySet<string> requested,
        SensorPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(preferences);

        return definitions
            .Where(definition => requested.Contains(definition.UniqueId)
                                 && (definition.Privacy == SensorPrivacy.Benign
                                     || preferences.IsEnabled(definition)))
            .Select(definition => definition.UniqueId)
            .ToHashSet(StringComparer.Ordinal);
    }
}
