using System.Text.Json;
using WindowsCompanion.Core.Models;
using WindowsCompanion.E2E.Tests.Fixtures;
using WindowsCompanion.Testing;

namespace WindowsCompanion.E2E.Tests;

[Collection(CompanionJourneyCollection.Name)]
public sealed class ConnectionJourneyTests
{
    [Fact]
    public async Task Clean_profile_completes_oauth_registration_and_initial_sync()
    {
        await CompanionJourneyFixture.RunAsync(
            "connection-first",
            "complete OAuth registration and initial sync",
            async fixture =>
            {
                var controller = await fixture.SignInAsync();

                Assert.Equal(ConnectionState.Connected, controller.State);
                Assert.True(controller.HasSavedSession);
                Assert.True(controller.Health.Healthy);
                Assert.Equal(fixture.Scenario.BaseUrl!.AbsoluteUri, controller.BaseUrl);
                Assert.Single(fixture.UriLauncher.Launched);
                Assert.Equal("/auth/authorize", fixture.UriLauncher.Launched[0].AbsolutePath);
                Assert.Single(fixture.Scenario.State.Registrations);
                Assert.Contains(
                    CompanionJourneyFixture.DeterministicSensorSource.EnabledId,
                    fixture.Scenario.State.SensorStates.Keys);
                Assert.DoesNotContain(
                    CompanionJourneyFixture.DeterministicSensorSource.OptionalId,
                    fixture.Scenario.State.SensorStates.Keys);
                Assert.True(fixture.Sensors.IsRunning);

                var kinds = fixture.Scenario.Interactions.Snapshot()
                    .Select(item => item.Kind)
                    .ToArray();
                Assert.Contains(FakeHaInteractionKind.Authorization, kinds);
                Assert.Contains(FakeHaInteractionKind.Token, kinds);
                Assert.Contains(FakeHaInteractionKind.Registration, kinds);
                Assert.Contains(FakeHaInteractionKind.Webhook, kinds);
                Assert.Contains(FakeHaInteractionKind.WebSocket, kinds);
            });
    }

    [Fact]
    public async Task Disconnect_and_reconnect_resume_without_duplicate_registration()
    {
        await CompanionJourneyFixture.RunAsync(
            "connection-reconnect",
            "disconnect and reconnect",
            async fixture =>
            {
                var controller = await fixture.ResumePreauthorizedAsync();
                var deviceId = Assert.Single(fixture.Scenario.State.Registrations).DeviceId;
                var boundary = fixture.LastInteractionSequence;

                await controller.DisconnectAsync();

                Assert.Equal(ConnectionState.Disconnected, controller.State);
                await fixture.Scenario.Interactions.WaitForAsync(
                    item => item.PathOrMessageType == "disconnected",
                    TimeSpan.FromSeconds(10),
                    afterSequence: boundary);

                await fixture.ReconnectAsync();

                Assert.Equal(ConnectionState.Connected, controller.State);
                Assert.Single(fixture.Scenario.State.Registrations);
                Assert.Equal(
                    deviceId,
                    Assert.Single(fixture.Scenario.State.Registrations).DeviceId);
                Assert.Contains(
                    fixture.Scenario.Interactions.Snapshot(),
                    item => item.Sequence > boundary
                            && item.PathOrMessageType == "update_registration");
            });
    }

    [Fact]
    public async Task Persisted_restart_reuses_registration_and_keeps_secrets_out_of_settings()
    {
        await CompanionJourneyFixture.RunAsync(
            "connection-restart",
            "resume persisted registration",
            async fixture =>
            {
                await fixture.ResumePreauthorizedAsync();
                var firstRegistration = Assert.Single(fixture.Scenario.State.Registrations);
                var firstConfig = fixture.LoadConfig();

                var restarted = await fixture.RestartAsync();
                var restartedConfig = fixture.LoadConfig();

                Assert.Equal(ConnectionState.Connected, restarted.State);
                Assert.Single(fixture.Scenario.State.Registrations);
                Assert.Equal(firstRegistration.DeviceId, restartedConfig.DeviceId);
                Assert.Equal(firstConfig.InstanceDeviceId, restartedConfig.InstanceDeviceId);
                Assert.Equal(fixture.Scenario.WebhookId, restartedConfig.WebhookId);
                Assert.Equal(
                    fixture.Scenario.RefreshToken,
                    fixture.SecretStore.Get(
                        WindowsCompanion_App.Services.AppConstants.RefreshTokenKey));

                var settings = await File.ReadAllTextAsync(fixture.SettingsPath);
                Assert.False(settings.Contains(
                    fixture.Scenario.AccessToken,
                    StringComparison.Ordinal));
                Assert.False(settings.Contains(
                    fixture.Scenario.RefreshToken,
                    StringComparison.Ordinal));
                Assert.False(settings.Contains(
                    fixture.Scenario.WebhookId,
                    StringComparison.Ordinal));

                var serializedInteractions = JsonSerializer.Serialize(
                    fixture.Scenario.Interactions.Snapshot());
                Assert.False(serializedInteractions.Contains(
                    fixture.Scenario.AccessToken,
                    StringComparison.Ordinal));
                Assert.False(serializedInteractions.Contains(
                    fixture.Scenario.RefreshToken,
                    StringComparison.Ordinal));
                Assert.False(serializedInteractions.Contains(
                    fixture.Scenario.WebhookId,
                    StringComparison.Ordinal));
            });
    }
}
