using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace WindowsCompanion.UI.Tests.Pages;

internal sealed class StatusPage(Window window)
{
    internal string Server => Find("Status.Server", ControlType.Text).Name;
    internal string Connection => Find("Status.Connection", ControlType.Text).Name;
    internal string Health => Find("Status.Health", ControlType.Text).Name;

    internal void WaitForConnection(string expected) =>
        AutomationWait.Until(
            () =>
            {
                var error = window.FindFirstDescendant(cf =>
                    cf.ByAutomationId("Connect.Error").And(cf.ByControlType(ControlType.Text)));
                if (error is { IsOffscreen: false } && !string.IsNullOrWhiteSpace(error.Name))
                    throw new InvalidOperationException($"Connection failed in the UI: {error.Name}");

                var element = window.FindFirstDescendant(cf =>
                    cf.ByAutomationId("Status.Connection").And(cf.ByControlType(ControlType.Text)));
                return element is { IsOffscreen: false } && element.Name == expected;
            },
            $"Connection state did not become '{expected}'.");

    internal void OpenSensors() => Button("Status.OpenSensors").Invoke();
    internal void OpenSettings() => Button("Status.OpenSettings").Invoke();

    internal void ConfirmDialog() => Button("Dialog.Primary").Invoke();
    internal void DismissDialog() => Button("Dialog.Cancel").Invoke();

    private Button Button(string automationId) =>
        Find(automationId, ControlType.Button).AsButton();

    private AutomationElement Find(string automationId, ControlType controlType) =>
        AutomationWait.Element(
            () => window.FindFirstDescendant(cf =>
                cf.ByAutomationId(automationId).And(cf.ByControlType(controlType))),
            automationId);
}
