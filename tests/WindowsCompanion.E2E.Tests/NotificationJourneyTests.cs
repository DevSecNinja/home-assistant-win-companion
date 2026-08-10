using WindowsCompanion.E2E.Tests.Fixtures;

namespace WindowsCompanion.E2E.Tests;

[Collection(CompanionJourneyCollection.Name)]
public sealed class NotificationJourneyTests
{
    [Fact]
    public async Task Push_notification_is_delivered_to_the_deterministic_sink()
    {
        await CompanionJourneyFixture.RunAsync(
            "notification-delivery",
            "deliver push notification",
            async fixture =>
            {
                await fixture.ResumePreauthorizedAsync();

                await fixture.Scenario.SendNotificationAsync(
                    "Synthetic title",
                    "Synthetic message");
                var notification = await fixture.Notifications.WaitForAsync(
                    item => item.Title == "Synthetic title");

                Assert.Equal("Synthetic message", notification.Message);
                Assert.Single(fixture.Notifications.Snapshot());
            });
    }

    [Fact]
    public async Task Confirmation_request_is_delivered_and_acknowledged()
    {
        await CompanionJourneyFixture.RunAsync(
            "notification-confirm",
            "deliver and acknowledge confirmation",
            async fixture =>
            {
                await fixture.ResumePreauthorizedAsync();
                const string confirmationId = "synthetic-confirmation";

                await fixture.Scenario.SendNotificationAsync(
                    "Confirm title",
                    "Confirm message",
                    confirmationId);
                var notification = await fixture.Notifications.WaitForAsync(
                    item => item.Message == "Confirm message");
                await fixture.Scenario.Interactions.WaitForAsync(
                    item => item.PathOrMessageType == "confirmation",
                    TimeSpan.FromSeconds(10));

                Assert.Equal("Confirm title", notification.Title);
                Assert.True(fixture.Scenario.State.ConfirmedNotifications.ContainsKey(
                    confirmationId));
            });
    }
}
