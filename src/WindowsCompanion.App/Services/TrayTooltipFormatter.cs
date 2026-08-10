using WindowsCompanion.Core.Updates;

namespace WindowsCompanion_App.Services;

/// <summary>Keeps update and connection health visible within Windows' tooltip limit.</summary>
internal static class TrayTooltipFormatter
{
    private const int MaximumLength = 127;

    internal static string Format(
        bool healthy,
        string healthSummary,
        SemanticVersion? availableVersion)
    {
        var health = healthy ? "Healthy" : healthSummary.Trim();
        var status = availableVersion is null
            ? health
            : $"Update v{availableVersion} available; {health}";
        var prefix = $"{Branding.ShortName} — ";
        var tooltip = prefix + status;
        if (tooltip.Length <= MaximumLength) return tooltip;

        var available = MaximumLength - prefix.Length - 1;
        return prefix + status[..available] + "…";
    }
}
