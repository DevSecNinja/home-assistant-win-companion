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
        HiddenWindowIsRestoredThroughTrayIcon(
            "hide and restore through tray single click",
            trayIcon => trayIcon.AsButton().Invoke());

    [UiTrayFact]
    public Task Hidden_window_is_restored_by_double_clicking_the_tray_icon() =>
        HiddenWindowIsRestoredThroughTrayIcon(
            "hide and restore through tray double click",
            trayIcon => trayIcon.DoubleClick(),
            suppressTrayLeftClick: true);

    [UiTrayFact]
    public Task Visible_background_window_is_activated_through_the_tray_icon() =>
        VisibleBackgroundWindowIsActivatedThroughTrayIcon(
            "activate background window through tray single click",
            trayIcon => trayIcon.AsButton().Invoke());

    [UiTrayFact]
    public Task Visible_background_window_is_activated_by_double_clicking_the_tray_icon() =>
        VisibleBackgroundWindowIsActivatedThroughTrayIcon(
            "activate background window through tray double click",
            trayIcon => trayIcon.DoubleClick(),
            suppressTrayLeftClick: true);

    [UiTrayFact]
    public Task Visible_background_window_is_activated_through_the_tray_menu() =>
        UiScenarioFixture.RunAsync(
            "ui-tray",
            "activate background window through tray menu",
            fixture =>
            {
                var status = Connect(fixture);
                var trayIcon = FindTrayIcon(fixture);

                UiCapabilities.FocusTaskbar();
                AutomationWait.Until(
                    () => !UiCapabilities.IsForegroundWindow(fixture.Window),
                    "The application window did not move to the background.");

                trayIcon.RightClick();
                var showItem = AutomationWait.Element(
                    () => fixture.Automation.GetDesktop().FindFirstDescendant(cf =>
                        cf.ByAutomationId("Tray.Show")
                            .And(cf.ByControlType(ControlType.MenuItem))),
                    "Show Companion tray menu item");
                showItem.AsMenuItem().Invoke();

                status.WaitForConnection("Connected");
                AutomationWait.Until(
                    () => UiCapabilities.IsForegroundWindow(fixture.Window),
                    "The application window did not return to the foreground.");
                Assert.True(UiCapabilities.IsWindowVisible(fixture.Window));
                return Task.CompletedTask;
            });

    private static Task VisibleBackgroundWindowIsActivatedThroughTrayIcon(
        string scenarioName,
        Action<AutomationElement> activateTrayIcon,
        bool suppressTrayLeftClick = false) =>
        UiScenarioFixture.RunAsync(
            "ui-tray",
            scenarioName,
            fixture =>
            {
                var status = Connect(fixture);
                var trayIcon = FindTrayIcon(fixture);

                UiCapabilities.FocusTaskbar();
                AutomationWait.Until(
                    () => !UiCapabilities.IsForegroundWindow(fixture.Window),
                    "The application window did not move to the background.");

                activateTrayIcon(trayIcon);

                status.WaitForConnection("Connected");
                AutomationWait.Until(
                    () => UiCapabilities.IsForegroundWindow(fixture.Window),
                    "The application window did not return to the foreground.");
                Assert.True(UiCapabilities.IsWindowVisible(fixture.Window));
                return Task.CompletedTask;
            },
            suppressTrayLeftClick: suppressTrayLeftClick);

    private static Task HiddenWindowIsRestoredThroughTrayIcon(
        string scenarioName,
        Action<AutomationElement> activateTrayIcon,
        bool suppressTrayLeftClick = false) =>
        UiScenarioFixture.RunAsync(
            "ui-tray",
            scenarioName,
            fixture =>
            {
                var status = Connect(fixture);

                fixture.Window.Close();
                AutomationWait.Until(
                    () => !UiCapabilities.IsWindowVisible(fixture.Window),
                    "The application window did not hide to the tray.");

                activateTrayIcon(FindTrayIcon(fixture));

                status.WaitForConnection("Connected");
                Assert.True(UiCapabilities.IsWindowVisible(fixture.Window));
                return Task.CompletedTask;
            },
            suppressTrayLeftClick: suppressTrayLeftClick);

    private static StatusPage Connect(UiScenarioFixture fixture)
    {
        new ConnectPage(fixture.Window).EnterUrl(fixture.Scenario.BaseUrl!.AbsoluteUri);
        var status = new StatusPage(fixture.Window);
        status.WaitForConnection("Connected");
        return status;
    }

    private static AutomationElement FindTrayIcon(UiScenarioFixture fixture) =>
        AutomationWait.Element(
            () => UiCapabilities.TrayAutomationRoots(fixture.Automation)
                .Select(root => root.FindFirstDescendant(cf =>
                    cf.ByName(fixture.TrayIdentity)
                        .And(cf.ByControlType(ControlType.Button))))
                .FirstOrDefault(element => element is not null),
            fixture.TrayIdentity);
}
