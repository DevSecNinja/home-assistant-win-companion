using WindowsCompanion.UI.Tests.Fixtures;
using WindowsCompanion.UI.Tests.Pages;

namespace WindowsCompanion.UI.Tests;

[Collection(UiTestCollection.Name)]
public sealed class StatusUiTests
{
    [UiFact]
    public Task Disconnect_reconnect_and_restart_preserve_the_registration() =>
        UiScenarioFixture.RunAsync(
            "ui-status",
            "disconnect reconnect and restart",
            async fixture =>
            {
                await ConnectAsync(fixture);
                var status = new StatusPage(fixture.Window);

                status.OpenSettings();
                var settings = new SettingsPage(fixture.Window);
                settings.WaitUntilVisible();
                settings.DisconnectOrReconnect();
                settings.Back();
                status.WaitForConnection("Disconnected");
                status.OpenSettings();
                settings.WaitUntilVisible();
                settings.DisconnectOrReconnect();
                settings.Back();
                status.WaitForConnection("Connected");

                var registrationCount = fixture.Scenario.State.Registrations.Count;
                await fixture.RestartAsync();
                new StatusPage(fixture.Window).WaitForConnection("Connected");

                Assert.Equal(registrationCount, fixture.Scenario.State.Registrations.Count);
            });

    private static async Task ConnectAsync(UiScenarioFixture fixture)
    {
        new ConnectPage(fixture.Window).EnterUrl(fixture.Scenario.BaseUrl!.AbsoluteUri);
        new StatusPage(fixture.Window).WaitForConnection("Connected");
        await fixture.Scenario.Interactions.WaitForAsync(
            interaction => interaction.PathOrMessageType == "update_sensor_states",
            TimeSpan.FromSeconds(20));
    }
}
