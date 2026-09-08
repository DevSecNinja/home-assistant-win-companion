using WindowsCompanion.Core.Sensors;

namespace WindowsCompanion.Core.Tests;

public sealed class SensorDefinitionTests
{
    [Fact]
    public void Search_terms_include_name_description_and_configured_aliases()
    {
        var definition = new SensorDefinition(
            "display_identity",
            "Display Identity",
            "Brand and model details.",
            SensorPrivacy.Sensitive,
            EnabledByDefault: false)
        {
            SearchAliases = ["Monitor", "Monitors"]
        };

        Assert.Equal(
            ["Display Identity", "Brand and model details.", "Monitor", "Monitors"],
            definition.SearchTerms);
        Assert.Contains(
            definition.SearchTerms,
            term => term.Contains("display", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            definition.SearchTerms,
            term => term.Contains("monitor", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("display", definition.SearchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("monitor", definition.SearchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Search_terms_without_aliases_preserve_existing_metadata()
    {
        var definition = new SensorDefinition(
            "battery_level",
            "Battery Level",
            "Charge percentage.",
            SensorPrivacy.Benign,
            EnabledByDefault: true);

        Assert.Equal(["Battery Level", "Charge percentage."], definition.SearchTerms);
        Assert.Equal("Battery Level\nCharge percentage.", definition.SearchText);
    }
}
