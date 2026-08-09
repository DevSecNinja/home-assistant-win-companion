namespace WindowsCompanion.Core.Sensors;

public static class HeadsetClassifier
{
    private static readonly string[] HeadsetTerms =
    [
        "headset",
        "headphone",
        "earbud",
        "airpod",
        "jabra",
        "poly ",
        "plantronics"
    ];

    public static bool IsHeadset(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && HeadsetTerms.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase));

    public static bool AnyHeadset(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        return names.Any(IsHeadset);
    }
}
