using WindowsCompanion.UI.Tests.Fixtures;
using WindowsCompanion.UI.Tests.Pages;

namespace WindowsCompanion.UI.Tests;

[Collection(UiTestCollection.Name)]
public sealed class FailureUiTests
{
    [UiFact]
    public Task Remove_server_confirmation_returns_to_connect() =>
        UiScenarioFixture.RunAsync(
            "ui-remove",
            "cancel then confirm server removal",
            async fixture =>
            {
                await ConnectAsync(fixture);
                var status = new StatusPage(fixture.Window);
                status.OpenSettings();
                var settings = new SettingsPage(fixture.Window);
                settings.WaitUntilVisible();

                settings.RemoveServer();
                status.DismissDialog();
                settings.WaitUntilVisible();
                settings.RemoveServer();
                status.ConfirmDialog();
                new ConnectPage(fixture.Window).WaitUntilVisible();

                Assert.False(File.Exists(Path.Combine(
                    fixture.ProfileDirectory,
                    "settings.json")));
            });

    [UiTheory]
    [InlineData("authentication")]
    [InlineData("connectivity")]
    public Task Failure_is_actionable_and_retry_remains_available(string failure) =>
        UiScenarioFixture.RunAsync(
            $"ui-failure-{failure}",
            $"{failure} failure and retry",
            fixture =>
            {
                var connect = new ConnectPage(fixture.Window);

                connect.EnterUrl(fixture.Scenario.BaseUrl!.AbsoluteUri);
                Assert.False(string.IsNullOrWhiteSpace(connect.WaitForError()));

                fixture.Scenario.Faults.Reset();
                connect.EnterUrl(fixture.Scenario.BaseUrl.AbsoluteUri);
                try
                {
                    new StatusPage(fixture.Window).WaitForConnection("Connected");
                }
                catch (TimeoutException ex)
                {
                    throw new TimeoutException(
                        $"{ex.Message} Error: {connect.VisibleError ?? "(none)"}"
                        + Environment.NewLine
                        + fixture.Scenario.Interactions.FormatHistory(),
                        ex);
                }

                return Task.CompletedTask;
            },
            scenario =>
            {
                if (failure == "authentication")
                    scenario.Faults.RejectAuthorizationCode = true;
                else
                    scenario.Faults.ApiUnavailable = true;
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
