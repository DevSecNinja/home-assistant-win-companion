using WindowsCompanion.Core.Updates;
using WindowsCompanion_App.Services;

namespace WindowsCompanion.App.Tests;

public class TrayTooltipFormatterTests
{
    [Fact]
    public void Update_tooltip_keeps_the_connection_health()
    {
        Assert.True(SemanticVersion.TryParse("0.4.0", out var update));

        var tooltip = TrayTooltipFormatter.Format(
            healthy: false,
            "Reconnecting",
            update);

        Assert.Equal(
            "Windows Companion — Update v0.4.0 available; Reconnecting",
            tooltip);
    }

    [Fact]
    public void Long_health_details_are_bounded_for_the_windows_tooltip()
    {
        Assert.True(SemanticVersion.TryParse("0.4.0", out var update));

        var tooltip = TrayTooltipFormatter.Format(
            healthy: false,
            new string('x', 200),
            update);

        Assert.Equal(127, tooltip.Length);
        Assert.EndsWith("…", tooltip, StringComparison.Ordinal);
        Assert.Contains("Update v0.4.0 available", tooltip, StringComparison.Ordinal);
    }
}
