using WindowsCompanion.E2E.Tests.Fixtures;

namespace WindowsCompanion.E2E.Tests;

[Collection(CompanionJourneyCollection.Name)]
public sealed class RepeatabilityTests
{
    [Fact]
    [Trait("Category", "Repeatability")]
    public async Task Healthy_journey_is_isolated_and_cleaned_for_ten_consecutive_runs()
    {
        var profilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var credentialResources = new HashSet<string>(StringComparer.Ordinal);

        for (var run = 1; run <= 10; run++)
        {
            await CompanionJourneyFixture.RunAsync(
                $"repeatability-{run}",
                $"complete repeatability run {run}",
                async fixture =>
                {
                    await fixture.ResumePreauthorizedAsync();

                    Assert.True(fixture.Scenario.BaseUrl!.IsLoopback);
                    Assert.NotEqual(8390, fixture.Scenario.BaseUrl.Port);
                    Assert.True(profilePaths.Add(fixture.ProfileDirectory));
                    Assert.True(credentialResources.Add(fixture.SecretStore.Resource));
                    Assert.Single(fixture.Scenario.State.Registrations);
                    Assert.Single(fixture.Scenario.State.SensorStates);
                });
        }
    }
}
