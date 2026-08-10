using System.Text.Json;
using System.Text.Json.Nodes;

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
        CancellationToken cancellationToken = default) =>
        await WriteAsync(
                scenario,
                directory,
                Array.Empty<string>(),
                cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Writes interaction history after redacting scenario and caller-supplied values.
    /// </summary>
    public static async Task<string> WriteAsync(
        FakeHaScenario scenario,
        string directory,
        IEnumerable<string> additionalSensitiveValues,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(additionalSensitiveValues);

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{scenario.ScenarioId}-interactions.json");
        var evidence = new
        {
            scenarioId = scenario.ScenarioId,
            lifecycle = scenario.Lifecycle.ToString(),
            interactions = scenario.Interactions.Snapshot()
        };
        var sensitiveValues = new[]
            {
                scenario.AccessToken,
                scenario.RefreshToken,
                scenario.WebhookId,
                scenario.AuthorizationCode
            }
            .Concat(additionalSensitiveValues)
            .Where(static value => !string.IsNullOrEmpty(value))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(static value => value.Length)
            .ToArray();
        var node = JsonSerializer.SerializeToNode(evidence, Options)
                   ?? throw new JsonException("Interaction evidence was empty.");
        var json = SanitizeNode(node, sensitiveValues)!.ToJsonString(Options);

        EnsureSanitized(json, sensitiveValues);
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        return path;
    }

    private static JsonNode? SanitizeNode(
        JsonNode? node,
        IReadOnlyList<string> sensitiveValues) =>
        node switch
        {
            JsonObject obj => new JsonObject(obj.Select(property =>
                KeyValuePair.Create(
                    Redact(property.Key, sensitiveValues),
                    SanitizeNode(property.Value, sensitiveValues)))),
            JsonArray array => new JsonArray(array
                .Select(item => SanitizeNode(item, sensitiveValues))
                .ToArray()),
            JsonValue value when value.TryGetValue<string>(out var text) =>
                JsonValue.Create(Redact(text, sensitiveValues)),
            null => null,
            _ => node.DeepClone()
        };

    private static string Redact(
        string value,
        IEnumerable<string> sensitiveValues)
    {
        foreach (var sensitiveValue in sensitiveValues)
        {
            value = value.Replace(
                sensitiveValue,
                "[REDACTED]",
                StringComparison.Ordinal);
        }
        return value;
    }

    private static void EnsureSanitized(
        string json,
        IReadOnlyList<string> sensitiveValues)
    {
        using var document = JsonDocument.Parse(json);
        if (EnumerateJsonStrings(document.RootElement).Any(value =>
                sensitiveValues.Any(sensitive =>
                    value.Contains(sensitive, StringComparison.Ordinal))))
        {
            throw new InvalidOperationException(
                "Fake Home Assistant evidence still contains a scenario secret.");
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
