using System.Text.Json;
using Microsoft.Extensions.Logging;
using WindowsCompanion.Core.Models;
using WindowsCompanion.E2E.Tests.Fixtures;

namespace WindowsCompanion.E2E.Tests;

[Collection(CompanionJourneyCollection.Name)]
public sealed class EvidenceContractTests
{
    [Fact]
    public async Task Journey_wrapper_captures_timeout_and_rethrows_the_original_exception()
    {
        var evidenceRoot = Path.Combine(
            AppContext.BaseDirectory,
            "contract-evidence",
            Guid.NewGuid().ToString("N"));
        var expected = new TimeoutException("synthetic journey timeout");
        const string scenarioId = "automatic-evidence-contract";
        const string failingStep = "await-connected-state";

        try
        {
            var actual = await Assert.ThrowsAsync<TimeoutException>(
                () => CompanionJourneyFixture.RunAsync(
                    scenarioId,
                    failingStep,
                    async fixture =>
                    {
                        await fixture.ResumePreauthorizedAsync();
                        throw expected;
                    },
                    evidenceRoot));

            Assert.Same(expected, actual);
            var evidenceDirectory = Assert.Single(
                Directory.GetDirectories(evidenceRoot, $"{scenarioId}-*"));
            var metadataPath = Path.Combine(evidenceDirectory, "scenario.json");
            Assert.True(File.Exists(metadataPath));
            using var metadata = JsonDocument.Parse(
                await File.ReadAllTextAsync(metadataPath));
            Assert.Equal(
                failingStep,
                metadata.RootElement.GetProperty("failingStep").GetString());
            Assert.Equal(
                typeof(TimeoutException).FullName,
                metadata.RootElement
                    .GetProperty("failure")
                    .GetProperty("type")
                    .GetString());
        }
        finally
        {
            if (Directory.Exists(evidenceRoot))
                Directory.Delete(evidenceRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Induced_failure_preserves_sanitized_scenario_interactions_and_app_log()
    {
        var evidenceRoot = Path.Combine(
            AppContext.BaseDirectory,
            "contract-evidence",
            Guid.NewGuid().ToString("N"));
        await using var fixture = await CompanionJourneyFixture.StartAsync(
            "evidence-contract",
            evidenceRoot);

        try
        {
            var controller = await fixture.ResumePreauthorizedAsync();
            const string failingStep = "verify-sensor-delivery";
            const string sensitiveSensorValue = "synthetic \"private\\sensor\nvalue";
            fixture.Evidence.RegisterSensitiveValue(sensitiveSensorValue);
            fixture.Scenario.Interactions.Record(
                WindowsCompanion.Testing.FakeHaInteractionKind.Webhook,
                "POST",
                "sensitive-key",
                new Dictionary<string, string>
                {
                    [sensitiveSensorValue] = "synthetic-value"
                });
            var sensorId = CompanionJourneyFixture.DeterministicSensorSource.EnabledId;
            fixture.Sensors.SetState(sensorId, sensitiveSensorValue);
            fixture.Scenario.Faults.RejectSensorUniqueId = sensorId;
            await controller.ForcePushAsync();
            Assert.False(controller.Health.Healthy);

            var logger = fixture.Evidence.LoggerFactory.CreateLogger<EvidenceContractTests>();
            logger.LogInformation(
                "Inducing {Step} with sensor value {SensorValue} and token {Token}.",
                failingStep,
                sensitiveSensorValue,
                fixture.Scenario.AccessToken);
            var failure = new InvalidOperationException(
                $"Induced failure for {fixture.Scenario.WebhookId}.");

            var artifacts = await fixture.Evidence.CaptureAsync(
                failingStep,
                controller.State,
                failure);

            Assert.True(File.Exists(artifacts.MetadataPath));
            Assert.True(File.Exists(artifacts.InteractionLogPath));
            Assert.True(File.Exists(artifacts.AppLogPath));

            using var interactions = JsonDocument.Parse(
                await File.ReadAllTextAsync(artifacts.InteractionLogPath));
            Assert.DoesNotContain(
                EnumerateJsonStrings(interactions.RootElement),
                value => value.Contains(sensitiveSensorValue, StringComparison.Ordinal));

            using var metadata = JsonDocument.Parse(
                await File.ReadAllTextAsync(artifacts.MetadataPath));
            Assert.Equal(
                fixture.Scenario.ScenarioId,
                metadata.RootElement.GetProperty("scenarioId").GetString());
            Assert.Equal(
                failingStep,
                metadata.RootElement.GetProperty("failingStep").GetString());
            Assert.Equal(
                ConnectionState.Connected.ToString(),
                metadata.RootElement.GetProperty("companionState").GetString());
            Assert.Equal(
                typeof(InvalidOperationException).FullName,
                metadata.RootElement
                    .GetProperty("failure")
                    .GetProperty("type")
                    .GetString());
            Assert.True(metadata.RootElement.GetProperty("interactionCount").GetInt32() > 0);

            var retained = string.Join(
                Environment.NewLine,
                await File.ReadAllTextAsync(artifacts.MetadataPath),
                await File.ReadAllTextAsync(artifacts.InteractionLogPath),
                await File.ReadAllTextAsync(artifacts.AppLogPath));
            Assert.Contains(failingStep, retained, StringComparison.Ordinal);
            Assert.Contains("[REDACTED]", retained, StringComparison.Ordinal);
            Assert.DoesNotContain(
                fixture.Scenario.AccessToken,
                retained,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                fixture.Scenario.RefreshToken,
                retained,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                fixture.Scenario.WebhookId,
                retained,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                fixture.Scenario.BaseUrl!.AbsoluteUri,
                retained,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                sensitiveSensorValue,
                retained,
                StringComparison.Ordinal);
        }

        finally
        {
            if (Directory.Exists(evidenceRoot))
                Directory.Delete(evidenceRoot, recursive: true);
        }
    }

    private static IEnumerable<string> EnumerateJsonStrings(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                yield return element.GetString() ?? string.Empty;
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    yield return property.Name;
                foreach (var value in EnumerateJsonStrings(property.Value))
                    yield return value;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                foreach (var value in EnumerateJsonStrings(item))
                    yield return value;
                break;
        }
    }
}
