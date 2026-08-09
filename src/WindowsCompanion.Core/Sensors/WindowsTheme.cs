namespace WindowsCompanion.Core.Sensors;

/// <summary>
/// The Windows Personalization colour settings that decide app and system theme.
/// </summary>
/// <param name="AppsUseLightTheme">The per-app preference (Settings › Personalization › Colors).</param>
/// <param name="SystemUsesLightTheme">The taskbar/Start preference, which Windows tracks separately.</param>
/// <param name="HighContrast">Whether a high-contrast theme is active, overriding both.</param>
public readonly record struct WindowsThemeState(
    bool AppsUseLightTheme,
    bool SystemUsesLightTheme,
    bool HighContrast)
{
    /// <summary>Windows' own default when the registry values are absent.</summary>
    public static WindowsThemeState Default => new(true, true, false);
}

/// <summary>
/// Derives the dark-mode sensor from the Personalization settings.
/// </summary>
/// <remarks>
/// A high-contrast theme is neither "dark mode on" nor a plain light theme: the
/// user has chosen an accessibility theme whose colours are set independently.
/// Rather than mislabel it, the binary sensor keeps following the app
/// preference - which Windows still reports - and the active high-contrast theme
/// is surfaced through the <c>theme</c> and <c>high_contrast</c> attributes so an
/// automation can tell the difference.
/// </remarks>
public static class WindowsThemeFormatter
{
    public const string Dark = "Dark";
    public const string Light = "Light";
    public const string HighContrast = "High Contrast";

    public static bool IsDarkMode(WindowsThemeState state) => !state.AppsUseLightTheme;

    public static string DescribeAppTheme(WindowsThemeState state) =>
        state.HighContrast ? HighContrast : IsDarkMode(state) ? Dark : Light;

    public static string DescribeSystemTheme(WindowsThemeState state) =>
        state.HighContrast ? HighContrast : state.SystemUsesLightTheme ? Light : Dark;

    public static string IconFor(WindowsThemeState state) => state switch
    {
        { HighContrast: true } => "mdi:contrast-circle",
        _ when IsDarkMode(state) => "mdi:weather-night",
        _ => "mdi:weather-sunny"
    };

    public static IDictionary<string, object> BuildAttributes(WindowsThemeState state) =>
        new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["theme"] = DescribeAppTheme(state),
            ["system_theme"] = DescribeSystemTheme(state),
            ["high_contrast"] = state.HighContrast
        };
}
