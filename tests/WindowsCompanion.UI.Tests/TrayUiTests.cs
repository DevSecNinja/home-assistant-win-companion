using FlaUI.Core.Definitions;
using FlaUI.Core.AutomationElements;
using WindowsCompanion.UI.Tests.Fixtures;
using WindowsCompanion.UI.Tests.Pages;

namespace WindowsCompanion.UI.Tests;

[Collection(UiTestCollection.Name)]
public sealed class TrayUiTests
{
    [UiTrayFact]
    public Task Hidden_window_is_restored_through_the_tray_icon() =>
        UiScenarioFixture.RunAsync(
            "ui-tray",
            "hide and restore through tray",
            fixture =>
            {
                new ConnectPage(fixture.Window).EnterUrl(fixture.Scenario.BaseUrl!.AbsoluteUri);
                var status = new StatusPage(fixture.Window);
                status.WaitForConnection("Connected");

                fixture.Window.Close();
                AutomationWait.Until(
                    () => !UiCapabilities.IsWindowVisible(fixture.Window),
                    "The application window did not hide to the tray.");

                var trayIcon = AutomationWait.Element(
                    () => UiCapabilities.TrayAutomationRoots(fixture.Automation)
                        .Select(root => root.FindFirstDescendant(cf =>
                            cf.ByName(fixture.TrayIdentity)
                                .And(cf.ByControlType(ControlType.Button))))
                        .FirstOrDefault(element => element is not null),
                    fixture.TrayIdentity);
                trayIcon.AsButton().Invoke();

                status.WaitForConnection("Connected");
                Assert.True(UiCapabilities.IsWindowVisible(fixture.Window));
                return Task.CompletedTask;
            });
}
