namespace WindowsCompanion.Core.Sensors;

/// <summary>
/// Normalises Windows locale and time-zone values into the names Home Assistant
/// users expect.
/// </summary>
/// <remarks>
/// The <c>locale</c> sensor reports the user's <em>regional format</em>
/// (Settings › Time &amp; language › Language &amp; region › Regional format),
/// as a BCP 47 name such as <c>nl-NL</c>. That is the setting that decides how
/// dates, numbers and the first day of the week are presented, which is what
/// automations key off. The display language and region are exposed as
/// attributes rather than as separate entities.
/// </remarks>
public static class LocaleFormatter
{
    public const string Unknown = "Unknown";

    /// <summary>Longest real BCP 47 tag is far below this; a guard, not a policy.</summary>
    public const int MaxLength = 40;

    /// <summary>
    /// Returns a BCP 47 language tag, or <see cref="Unknown"/> when the value is
    /// missing or the invariant culture (which names no locale at all).
    /// </summary>
    public static string Describe(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName)) return Unknown;

        var name = cultureName.Trim().Replace('_', '-');
        if (name.Length == 0 || name.Length > MaxLength) return Unknown;

        // BCP 47 subtags are alphanumeric; anything else is not a locale name.
        return name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') ? name : Unknown;
    }

    /// <summary>
    /// Prefers the IANA name Home Assistant itself uses ("Europe/Amsterdam") and
    /// falls back to the Windows id when Windows has no IANA equivalent.
    /// </summary>
    /// <remarks>
    /// Windows zones cover whole regions, so the CLDR mapping returns that
    /// region's canonical city rather than the user's own: a PC set to
    /// "W. Europe Standard Time" in Amsterdam reports <c>Europe/Berlin</c>. The
    /// offset and DST rules are identical, which is what automations act on.
    /// </remarks>
    public static string DescribeTimeZone(string? ianaId, string? windowsId)
    {
        if (!string.IsNullOrWhiteSpace(ianaId)) return Shorten(ianaId.Trim());
        if (!string.IsNullOrWhiteSpace(windowsId)) return Shorten(windowsId.Trim());
        return Unknown;
    }

    private static string Shorten(string value) =>
        value.Length <= 255 ? value : value[..255];
}
