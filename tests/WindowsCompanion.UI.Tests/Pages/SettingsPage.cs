using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace WindowsCompanion.UI.Tests.Pages;

internal sealed class SettingsPage(Window window)
{
    internal void WaitUntilVisible() =>
        AutomationWait.Until(
            () => window.FindFirstDescendant(cf =>
                cf.ByAutomationId("Settings.Back").And(cf.ByControlType(ControlType.Button)))
                is { IsOffscreen: false },
            "Settings page did not become visible.");

    internal void SyncSensors() => Button("Settings.SyncSensors").Invoke();
    internal void DisconnectOrReconnect() => Button("Settings.Disconnect").Invoke();
    internal void RemoveServer() => Button("Settings.RemoveServer").Invoke();
    internal void Back() => Button("Settings.Back").Invoke();

    private Button Button(string automationId) =>
        AutomationWait.Element(
                () => window.FindFirstDescendant(cf =>
                    cf.ByAutomationId(automationId).And(cf.ByControlType(ControlType.Button))),
                automationId)
            .AsButton();
}
