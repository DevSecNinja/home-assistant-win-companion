using WindowsCompanion.UI.Tests.Fixtures;
using WindowsCompanion.UI.Tests.Pages;

namespace WindowsCompanion.UI.Tests;

[Collection(UiTestCollection.Name)]
public sealed class ConnectUiTests
{
    [UiFact]
    public Task Clean_launch_validates_an_empty_server_url() =>
        UiScenarioFixture.RunAsync(
            "ui-empty-url",
            "validate empty server URL",
            fixture =>
            {
                var page = new ConnectPage(fixture.Window);
                page.WaitUntilVisible();
                page.SignIn.Invoke();

                Assert.Contains("enter", page.WaitForError(), StringComparison.OrdinalIgnoreCase);
                Assert.Empty(fixture.Scenario.State.Registrations);
                return Task.CompletedTask;
            });

    [UiFact]
    public Task Sign_in_reaches_connected_status() =>
        UiScenarioFixture.RunAsync(
            "ui-connect",
            "sign in and reach connected status",
            fixture =>
            {
                var connect = new ConnectPage(fixture.Window);
                connect.EnterUrl(fixture.Scenario.BaseUrl!.AbsoluteUri);

                var status = new StatusPage(fixture.Window);
                status.WaitForConnection("Connected");
                Assert.Equal(
                    fixture.Scenario.BaseUrl.AbsoluteUri.TrimEnd('/'),
                    status.Server.TrimEnd('/'));
                Assert.NotEmpty(fixture.Scenario.State.Registrations);
                return Task.CompletedTask;
            });
}
