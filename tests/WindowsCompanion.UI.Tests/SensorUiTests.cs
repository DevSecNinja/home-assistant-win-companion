using WindowsCompanion.Testing;
using WindowsCompanion.UI.Tests.Fixtures;
using WindowsCompanion.UI.Tests.Pages;

namespace WindowsCompanion.UI.Tests;

[Collection(UiTestCollection.Name)]
public sealed class SensorUiTests
{
    [UiFact]
    public Task Sensor_preview_refreshes_while_page_remains_open() =>
        UiScenarioFixture.RunAsync(
            "ui-sensor-preview-refresh",
            "refresh sensor preview automatically",
            async fixture =>
            {
                new ConnectPage(fixture.Window).EnterUrl(fixture.Scenario.BaseUrl!.AbsoluteUri);
                var status = new StatusPage(fixture.Window);
                status.WaitForConnection("Connected");
                status.OpenSensors();

                var sensors = new SensorsPage(fixture.Window);
                sensors.WaitUntilVisible();
                var initial = sensors.Preview("battery_level");

                await Task.Run(() => AutomationWait.Until(
                    () => !string.Equals(
                        sensors.Preview("battery_level"),
                        initial,
                        StringComparison.Ordinal),
                    "Battery preview did not refresh while the Sensors page remained open."));
            });

    [UiFact]
    public Task Sensor_toggle_and_update_now_reach_home_assistant() =>
        UiScenarioFixture.RunAsync(
            "ui-sensors",
            "change sensor setting and update now",
            async fixture =>
            {
                new ConnectPage(fixture.Window).EnterUrl(fixture.Scenario.BaseUrl!.AbsoluteUri);
                var status = new StatusPage(fixture.Window);
                status.WaitForConnection("Connected");
                await fixture.Scenario.Interactions.WaitForAsync(
                    interaction => interaction.PathOrMessageType == "update_sensor_states",
                    TimeSpan.FromSeconds(20));
                var sequence = fixture.Scenario.Interactions.Snapshot().Last().Sequence;

                status.OpenSensors();
                var sensors = new SensorsPage(fixture.Window);
                sensors.WaitUntilVisible();
                Assert.True(sensors.IsEnabled("battery_level"));
                sensors.SetEnabled("battery_level", false);
                await fixture.Scenario.Interactions.WaitForAsync(
                    interaction => interaction.Kind == FakeHaInteractionKind.Webhook
                                   && interaction.PathOrMessageType == "register_sensor",
                    TimeSpan.FromSeconds(20),
                    afterSequence: sequence);
                sensors.Save();

                sequence = fixture.Scenario.Interactions.Snapshot().Last().Sequence;
                status.OpenSettings();
                var settings = new SettingsPage(fixture.Window);
                settings.WaitUntilVisible();
                settings.SyncSensors();
                await fixture.Scenario.Interactions.WaitForAsync(
                    interaction => interaction.PathOrMessageType == "update_sensor_states",
                    TimeSpan.FromSeconds(20),
                    afterSequence: sequence);
                Assert.True(fixture.Scenario.State.RegisteredSensors.TryGetValue(
                    "battery_level",
                    out var registration));
                Assert.True(registration.GetProperty("disabled").GetBoolean());
            });
}
