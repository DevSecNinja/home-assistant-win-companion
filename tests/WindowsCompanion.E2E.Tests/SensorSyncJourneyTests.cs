using WindowsCompanion.E2E.Tests.Fixtures;

namespace WindowsCompanion.E2E.Tests;

[Collection(CompanionJourneyCollection.Name)]
public sealed class SensorSyncJourneyTests
{
    [Fact]
    public async Task Enabling_an_opt_in_sensor_registers_and_synchronizes_it()
    {
        await CompanionJourneyFixture.RunAsync(
            "sensor-enable",
            "enable and synchronize opt-in sensor",
            async fixture =>
            {
                var controller = await fixture.ResumePreauthorizedAsync();
                var optionalId = CompanionJourneyFixture.DeterministicSensorSource.OptionalId;

                Assert.False(controller.Catalog!.IsEnabled(optionalId));
                Assert.False(fixture.Scenario.State.RegisteredSensors.ContainsKey(optionalId));
                var boundary = fixture.LastInteractionSequence;

                controller.Catalog.SetEnabled(optionalId, true);
                await controller.ApplySensorChangesAsync();

                Assert.True(controller.Catalog.IsEnabled(optionalId));
                Assert.True(fixture.Scenario.State.RegisteredSensors.TryGetValue(
                    optionalId,
                    out var registration));
                Assert.False(registration.GetProperty("disabled").GetBoolean());
                Assert.Equal(
                    7,
                    fixture.Scenario.State.SensorStates[optionalId]
                        .GetProperty("state")
                        .GetInt32());
                Assert.Contains(
                    fixture.Scenario.Interactions.Snapshot(),
                    item => item.Sequence > boundary
                            && item.PathOrMessageType == "register_sensor");
                Assert.Contains(optionalId, fixture.LoadConfig().RegisteredSensors.Keys);
            });
    }

    [Fact]
    public async Task Disabling_a_registered_sensor_retires_it_and_stops_transmitting_it()
    {
        await CompanionJourneyFixture.RunAsync(
            "sensor-disable",
            "disable and retire registered sensor",
            async fixture =>
            {
                var controller = await fixture.ResumePreauthorizedAsync();
                var enabledId = CompanionJourneyFixture.DeterministicSensorSource.EnabledId;
                var boundary = fixture.LastInteractionSequence;

                controller.Catalog!.SetEnabled(enabledId, false);
                await controller.ApplySensorChangesAsync();

                Assert.False(controller.Catalog.IsEnabled(enabledId));
                Assert.True(fixture.Scenario.State.RegisteredSensors.TryGetValue(
                    enabledId,
                    out var retired));
                Assert.True(retired.GetProperty("disabled").GetBoolean());
                Assert.DoesNotContain(enabledId, fixture.LoadConfig().RegisteredSensors.Keys);
                Assert.DoesNotContain(
                    fixture.Scenario.Interactions.Snapshot(),
                    item => item.Sequence > boundary
                            && item.PathOrMessageType == "update_sensor_states");
                Assert.False(fixture.Sensors.IsRunning);
                Assert.Equal(1, fixture.Sensors.StopCount);
            });
    }

    [Fact]
    public async Task Manual_push_synchronizes_the_latest_deterministic_state()
    {
        await CompanionJourneyFixture.RunAsync(
            "sensor-state",
            "force latest sensor state",
            async fixture =>
            {
                var controller = await fixture.ResumePreauthorizedAsync();
                var enabledId = CompanionJourneyFixture.DeterministicSensorSource.EnabledId;
                var boundary = fixture.LastInteractionSequence;
                fixture.Sensors.SetState(enabledId, "synthetic-updated");

                await controller.ForcePushAsync();

                Assert.Equal(
                    "synthetic-updated",
                    fixture.Scenario.State.SensorStates[enabledId]
                        .GetProperty("state")
                        .GetString());
                Assert.Contains(
                    fixture.Scenario.Interactions.Snapshot(),
                    item => item.Sequence > boundary
                            && item.PathOrMessageType == "update_sensor_states");
            });
    }
}
