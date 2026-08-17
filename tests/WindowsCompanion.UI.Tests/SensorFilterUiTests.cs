using WindowsCompanion.UI.Tests.Fixtures;
using WindowsCompanion.UI.Tests.Pages;

namespace WindowsCompanion.UI.Tests;

[Collection(UiTestCollection.Name)]
public sealed class SensorFilterUiTests
{
    [UiFact]
    public Task Filter_narrows_list_and_clear_restores_all() =>
        UiScenarioFixture.RunAsync(
            "ui-sensor-filter",
            "filter sensors by name, clear restores all",
            async fixture =>
            {
                new ConnectPage(fixture.Window).EnterUrl(fixture.Scenario.BaseUrl!.AbsoluteUri);
                var status = new StatusPage(fixture.Window);
                status.WaitForConnection("Connected");
                await fixture.Scenario.Interactions.WaitForAsync(
                    interaction => interaction.PathOrMessageType == "update_sensor_states",
                    TimeSpan.FromSeconds(20));

                status.OpenSensors();
                var sensors = new SensorsPage(fixture.Window);
                sensors.WaitUntilVisible();

                // Battery sensor should be visible initially
                Assert.True(sensors.IsSensorVisible("battery_level"));

                // Filter with a term that matches battery (case-insensitive)
                sensors.SetFilter("BATTERY");
                AutomationWait.Until(
                    () => sensors.IsSensorVisible("battery_level"),
                    "Battery sensor should remain visible with matching filter.");

                // Filter with non-matching term shows empty state
                sensors.SetFilter("zzz_no_match_zzz");
                AutomationWait.Until(
                    () => sensors.IsEmptyStateVisible(),
                    "Empty state should be visible when no sensors match.");
                Assert.False(sensors.IsSensorVisible("battery_level"));

                // Clear restores all
                sensors.ClearFilter();
                AutomationWait.Until(
                    () => sensors.IsSensorVisible("battery_level"),
                    "Battery sensor should reappear after clearing filter.");
                Assert.False(sensors.IsEmptyStateVisible());

                sensors.Save();
            });
}
