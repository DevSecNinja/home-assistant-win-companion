using System.Text.Json;

namespace WindowsCompanion.Testing;

/// <summary>Writes sanitized fake-server interaction evidence for a scenario.</summary>
public static class FakeHaEvidenceWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    /// <summary>Writes the scenario interaction history as JSON and returns its path.</summary>
    public static async Task<string> WriteAsync(
        FakeHaScenario scenario,
        string directory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{scenario.ScenarioId}-interactions.json");
        var evidence = new
        {
            scenarioId = scenario.ScenarioId,
            lifecycle = scenario.Lifecycle.ToString(),
            interactions = scenario.Interactions.Snapshot()
        };
        var json = JsonSerializer.Serialize(evidence, Options);

        EnsureSanitized(json, scenario);
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        return path;
    }

    private static void EnsureSanitized(string json, FakeHaScenario scenario)
    {
        var sensitiveValues = new[]
        {
            scenario.AccessToken,
            scenario.RefreshToken,
            scenario.WebhookId
        };
        if (sensitiveValues.Any(value =>
                json.Contains(value, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Fake Home Assistant evidence still contains a scenario secret.");
        }
    }
}
