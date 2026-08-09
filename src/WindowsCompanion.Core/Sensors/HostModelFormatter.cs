namespace WindowsCompanion.Core.Sensors;

/// <summary>
/// Turns the raw SMBIOS manufacturer and product strings into the model name a
/// user would recognise, e.g. "Dell Precision 5560".
/// </summary>
/// <remarks>
/// Only the manufacturer and product name participate. Serial numbers, service
/// tags, asset tags, SKU numbers, BIOS identifiers and machine GUIDs are never
/// read, so nothing here can leak a unique hardware identifier.
///
/// OEMs frequently leave the SMBIOS fields at their template values
/// ("System manufacturer", "To Be Filled By O.E.M."). Those are worse than no
/// value at all, so they are dropped rather than reported.
/// </remarks>
public static class HostModelFormatter
{
    public const string Unknown = "Unknown";

    /// <summary>Long enough for any real model name, short of HA's 255 limit.</summary>
    public const int MaxLength = 100;

    private static readonly HashSet<string> Placeholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "system manufacturer",
        "system product name",
        "system version",
        "system model",
        "to be filled by o.e.m.",
        "to be filled by oem",
        "default string",
        "not applicable",
        "not specified",
        "no enclosure",
        "unknown",
        "none",
        "oem",
        "o.e.m.",
        "n/a",
        "na",
        "invalid",
        "empty",
        "-",
        "*"
    };

    /// <summary>
    /// Combines manufacturer and model, avoiding the duplication OEMs cause by
    /// repeating their own name in the product string ("HP" + "HP EliteBook").
    /// </summary>
    public static string Describe(string? manufacturer, string? model)
    {
        var vendor = Clean(manufacturer);
        var product = Clean(model);

        if (vendor is null && product is null) return Unknown;
        if (product is null) return Truncate(vendor!);
        if (vendor is null) return Truncate(product);

        return Truncate(
            product.StartsWith(vendor, StringComparison.OrdinalIgnoreCase)
                ? product
                : $"{vendor} {product}");
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var collapsed = string.Join(' ', value.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return collapsed.Length == 0 || Placeholders.Contains(collapsed) ? null : collapsed;
    }

    private static string Truncate(string value) =>
        value.Length <= MaxLength ? value : value[..MaxLength].TrimEnd();
}
