using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace WindowsCompanion.UI.Tests.Pages;

internal sealed class SensorsPage(Window window)
{
    internal void WaitUntilVisible() =>
        AutomationWait.Until(
            () => window.FindFirstDescendant(cf =>
                cf.ByAutomationId("Sensors.Save").And(cf.ByControlType(ControlType.Button)))
                is { IsOffscreen: false },
            "Sensors page did not become visible.");

    internal void SetFilter(string text)
    {
        var searchBox = AutomationWait.Element(
            () => window.FindFirstDescendant(cf =>
                cf.ByAutomationId("Sensors.SearchBox")),
            "Sensors.SearchBox");
        searchBox.AsTextBox().Text = text;
    }

    internal void ClearFilter() => SetFilter(string.Empty);

    internal bool IsEmptyStateVisible()
    {
        var element = window.FindFirstDescendant(cf =>
            cf.ByAutomationId("Sensors.SearchEmpty"));
        return element is { IsOffscreen: false };
    }

    internal bool IsSensorVisible(string sensorId)
    {
        var element = window.FindFirstDescendant(cf =>
            cf.ByAutomationId($"Sensors.Toggle.{sensorId}"));
        return element is { IsOffscreen: false };
    }

    internal bool IsEnabled(string sensorId)
    {
        var element = Toggle(sensorId);
        return element.AsToggleButton().IsToggled == true;
    }

    internal string Preview(string sensorId) =>
        AutomationWait.Element(
                () => window.FindFirstDescendant(cf =>
                    cf.ByAutomationId($"Sensors.Preview.{sensorId}")),
                $"Sensors.Preview.{sensorId}")
            .Name;

    internal void SetEnabled(string sensorId, bool enabled)
    {
        var toggle = Toggle(sensorId).AsToggleButton();
        if (toggle.IsToggled != enabled) toggle.Toggle();
        AutomationWait.Until(
            () => Toggle(sensorId).AsToggleButton().IsToggled == enabled,
            $"Sensor '{sensorId}' did not reach the requested toggle state.");
    }

    internal void Save() =>
        AutomationWait.Element(
                () => window.FindFirstDescendant(cf =>
                    cf.ByAutomationId("Sensors.Save").And(cf.ByControlType(ControlType.Button))),
                "Sensors.Save")
            .AsButton()
            .Invoke();

    private AutomationElement Toggle(string sensorId) =>
        AutomationWait.Element(
            () => window.FindFirstDescendant(cf =>
                cf.ByAutomationId($"Sensors.Toggle.{sensorId}")
                    .And(cf.ByControlType(ControlType.Button))),
            $"Sensors.Toggle.{sensorId}");
}
