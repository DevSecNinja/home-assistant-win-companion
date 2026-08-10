using WindowsCompanion.Core.Models;
using WindowsCompanion.E2E.Tests.Fixtures;

namespace WindowsCompanion.E2E.Tests;

[Collection(CompanionJourneyCollection.Name)]
public sealed class RecoveryJourneyTests
{
    [Fact]
    public async Task Rejected_sensor_operation_surfaces_failure_and_recovers_on_manual_push()
    {
        await CompanionJourneyFixture.RunAsync(
            "recovery-operation",
            "recover rejected sensor operation",
            async fixture =>
            {
                var enabledId = CompanionJourneyFixture.DeterministicSensorSource.EnabledId;
                fixture.Scenario.Faults.RejectSensorUniqueId = enabledId;
                var controller = await fixture.ResumePreauthorizedAsync(waitForReady: false);

                await controller.ForcePushAsync();

                Assert.False(controller.Health.Healthy);
                Assert.Equal(ConnectionState.Connecting, controller.State);
                Assert.False(fixture.Scenario.State.SensorStates.ContainsKey(enabledId));
                var boundary = fixture.LastInteractionSequence;
                fixture.Scenario.Faults.RejectSensorUniqueId = null;

                await controller.ForcePushAsync();
                await fixture.Scenario.Interactions.WaitForAsync(
                    item => item.Sequence > boundary
                            && item.PathOrMessageType == "update_sensor_states",
                    TimeSpan.FromSeconds(10),
                    afterSequence: boundary);
                await fixture.WaitForStateAsync(ConnectionState.Connected);

                Assert.True(controller.Health.Healthy);
                Assert.Equal(
                    "synthetic-ready",
                    fixture.Scenario.State.SensorStates[enabledId]
                        .GetProperty("state")
                        .GetString());
            });
    }

    [Fact]
    public async Task Rejected_refresh_enters_auth_error_and_reconnect_recovers()
    {
        await CompanionJourneyFixture.RunAsync(
            "recovery-refresh",
            "recover rejected refresh token",
            async fixture =>
            {
                await fixture.ResumePreauthorizedAsync();
                await fixture.DisposeControllerAsync();
                fixture.Scenario.Faults.RejectRefreshToken = true;
                var controller = fixture.CreateController();

                Assert.True(await controller.TryResumeAsync());
                await fixture.WaitForStateAsync(ConnectionState.AuthError);

                Assert.Equal(ConnectionState.AuthError, controller.State);
                var boundary = fixture.LastInteractionSequence;
                fixture.Scenario.Faults.RejectRefreshToken = false;

                await fixture.ReconnectAsync();

                Assert.Equal(ConnectionState.Connected, controller.State);
                Assert.Contains(
                    fixture.Scenario.Interactions.Snapshot(),
                    item => item.Sequence > boundary
                            && item.PathOrMessageType == "refresh_token"
                            && item.Outcome == "Success");
            });
    }

    [Fact]
    public async Task Interrupted_push_channel_reconnects_and_delivers_again()
    {
        await CompanionJourneyFixture.RunAsync(
            "recovery-websocket",
            "recover interrupted push channel",
            async fixture =>
            {
                fixture.Network.Set(new NetworkContext(NetworkKind.Unknown));
                var controller = await fixture.ResumePreauthorizedAsync();
                var boundary = fixture.LastInteractionSequence;

                await fixture.Scenario.CloseWebSocketsAsync();
                var disconnected = await fixture.Scenario.Interactions.WaitForAsync(
                    item => item.PathOrMessageType == "disconnected",
                    TimeSpan.FromSeconds(10),
                    afterSequence: boundary);
                await fixture.Scenario.Interactions.WaitForAsync(
                    item => item.PathOrMessageType == "push_subscribed",
                    TimeSpan.FromSeconds(10),
                    afterSequence: disconnected.Sequence);

                await fixture.Scenario.SendNotificationAsync(
                    "Recovered title",
                    "Recovered message");
                var notification = await fixture.Notifications.WaitForAsync(
                    item => item.Message == "Recovered message");

                Assert.Equal("Recovered title", notification.Title);
                Assert.NotEqual(ConnectionState.AuthError, controller.State);
                Assert.True(
                    fixture.Scenario.Interactions.Snapshot().Count(
                        item => item.PathOrMessageType == "push_subscribed") >= 2);
            });
    }
}
