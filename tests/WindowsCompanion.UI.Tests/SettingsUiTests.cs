using WindowsCompanion.UI.Tests.Fixtures;
using WindowsCompanion.UI.Tests.Pages;

namespace WindowsCompanion.UI.Tests;

[Collection(UiTestCollection.Name)]
public sealed class SettingsUiTests
{
    [UiFact]
    public Task Settings_shows_the_installed_app_version() =>
        UiScenarioFixture.RunAsync(
            "ui-settings",
            "settings shows installed version",
            async fixture =>
            {
                new ConnectPage(fixture.Window).EnterUrl(fixture.Scenario.BaseUrl!.AbsoluteUri);
                var status = new StatusPage(fixture.Window);
                status.WaitForConnection("Connected");
                await fixture.Scenario.Interactions.WaitForAsync(
                    interaction => interaction.PathOrMessageType == "update_sensor_states",
                    TimeSpan.FromSeconds(20));

                status.OpenSettings();
                var settings = new SettingsPage(fixture.Window);
                settings.WaitUntilVisible();

                var installedVersion = settings.InstalledVersion();
                Assert.False(string.IsNullOrWhiteSpace(installedVersion));
                Assert.NotEqual("—", installedVersion);
            });
}
