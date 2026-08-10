using FlaUI.Core.Definitions;
using WindowsCompanion.UI.Tests.Fixtures;
using WindowsCompanion.UI.Tests.Pages;

namespace WindowsCompanion.UI.Tests;

[Collection(UiTestCollection.Name)]
public sealed class NotificationUiTests
{
    [UiNotificationFact]
    public Task Native_notification_is_delivered_when_the_shell_supports_it() =>
        UiScenarioFixture.RunAsync(
            "ui-notification",
            "deliver native notification",
            async fixture =>
            {
                new ConnectPage(fixture.Window).EnterUrl(fixture.Scenario.BaseUrl!.AbsoluteUri);
                new StatusPage(fixture.Window).WaitForConnection("Connected");
                var confirmationId = $"ui-confirm-{Guid.NewGuid():N}";
                var title = $"Companion UI notification {Guid.NewGuid():N}";

                await fixture.Scenario.SendNotificationAsync(
                    title,
                    "Synthetic notification body",
                    confirmationId);

                var desktop = fixture.Automation.GetDesktop();
                AutomationWait.Element(
                    () => desktop.FindFirstDescendant(cf =>
                        cf.ByName(title).And(cf.ByControlType(ControlType.Text))),
                    title);
                await fixture.Scenario.Interactions.WaitForAsync(
                    interaction =>
                        interaction.Kind
                        == WindowsCompanion.Testing.FakeHaInteractionKind.Notification
                        && interaction.PathOrMessageType == "confirmation",
                    TimeSpan.FromSeconds(20));
                Assert.Contains(
                    confirmationId,
                    fixture.Scenario.State.ConfirmedNotifications.Keys);
            });
}
