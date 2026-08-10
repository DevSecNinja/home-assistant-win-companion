using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Testing;

namespace WindowsCompanion.E2E.Tests.Fixtures;

internal sealed record FailureEvidenceArtifacts(
    string Directory,
    string MetadataPath,
    string InteractionLogPath,
    string AppLogPath);

internal sealed class FailureEvidence : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly FakeHaScenario? _scenario;
    private readonly string _scenarioId;
    private readonly object _sensitiveGate = new();
    private readonly HashSet<string> _sensitiveValues = new(StringComparer.Ordinal);
    private readonly ILogger _log;
    private bool _disposed;

    public FailureEvidence(
        FakeHaScenario scenario,
        string profileDirectory,
        string? evidenceRoot = null)
        : this(
            scenario?.ScenarioId
            ?? throw new ArgumentNullException(nameof(scenario)),
            scenario,
            profileDirectory,
            evidenceRoot)
    {
    }

    public FailureEvidence(
        string scenarioId,
        string profileDirectory,
        string? evidenceRoot = null)
        : this(scenarioId, null, profileDirectory, evidenceRoot)
    {
    }

    private FailureEvidence(
        string scenarioId,
        FakeHaScenario? scenario,
        string profileDirectory,
        string? evidenceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioId);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);
        if (!string.Equals(
                scenarioId,
                Path.GetFileName(scenarioId),
                StringComparison.Ordinal)
            || scenarioId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Scenario id must be safe for file names.", nameof(scenarioId));
        }

        _scenario = scenario;
        _scenarioId = scenarioId;
        if (scenario is not null)
        {
            RegisterSensitiveValue(scenario.AccessToken);
            RegisterSensitiveValue(scenario.RefreshToken);
            RegisterSensitiveValue(scenario.WebhookId);
            RegisterSensitiveValue(scenario.BaseUrl?.AbsoluteUri);
        }

        LiveAppLogPath = Path.Combine(profileDirectory, "logs", "app.log");
        EvidenceDirectory = Path.Combine(
            evidenceRoot ?? Path.Combine(
                Environment.CurrentDirectory,
                "TestResults",
                "evidence"),
            $"{scenarioId}-{Guid.NewGuid():N}");

        LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(new IsolatedFileLoggerProvider(
                LiveAppLogPath,
                Sanitize));
        });
        _log = LoggerFactory.CreateLogger<FailureEvidence>();
    }

    public string ScenarioId => _scenarioId;
    public string LiveAppLogPath { get; }
    public string EvidenceDirectory { get; }
    public ILoggerFactory LoggerFactory { get; }

    public void RegisterSensitiveValue(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        lock (_sensitiveGate) _sensitiveValues.Add(value);
    }

    public async Task<FailureEvidenceArtifacts> CaptureAsync(
        string failingStep,
        ConnectionState companionState,
        Exception failure,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(failingStep);
        ArgumentNullException.ThrowIfNull(failure);

        Directory.CreateDirectory(EvidenceDirectory);
        _log.LogError(
            failure,
            "Scenario {ScenarioId} failed at {FailingStep} in state {CompanionState}.",
            ScenarioId,
            failingStep,
            companionState);

        var interactionPath = await WriteInteractionsAsync(cancellationToken)
            .ConfigureAwait(false);
        await SanitizeFileAsync(interactionPath, cancellationToken).ConfigureAwait(false);

        var appLogPath = Path.Combine(EvidenceDirectory, "app.log");
        var appLog = File.Exists(LiveAppLogPath)
            ? await File.ReadAllTextAsync(LiveAppLogPath, cancellationToken).ConfigureAwait(false)
            : string.Empty;
        await File.WriteAllTextAsync(
                appLogPath,
                Sanitize(appLog),
                cancellationToken)
            .ConfigureAwait(false);

        var metadataPath = Path.Combine(EvidenceDirectory, "scenario.json");
        var metadata = new
        {
            scenarioId = ScenarioId,
            scenarioLifecycle = _scenario?.Lifecycle.ToString() ?? "Unavailable",
            failingStep,
            companionState = companionState.ToString(),
            capturedAtUtc = DateTimeOffset.UtcNow,
            failure = new
            {
                type = failure.GetType().FullName,
                message = Sanitize(failure.Message)
            },
            interactionCount = _scenario?.Interactions.Snapshot().Count ?? 0,
            artifacts = new
            {
                interactions = Path.GetFileName(interactionPath),
                appLog = Path.GetFileName(appLogPath)
            }
        };
        var metadataJson = SanitizeJson(JsonSerializer.Serialize(metadata, JsonOptions));
        await File.WriteAllTextAsync(
                metadataPath,
                metadataJson,
                cancellationToken)
            .ConfigureAwait(false);

        EnsureSanitized(interactionPath, appLogPath, metadataPath);
        return new FailureEvidenceArtifacts(
            EvidenceDirectory,
            metadataPath,
            interactionPath,
            appLogPath);
    }

    private async Task<string> WriteInteractionsAsync(CancellationToken cancellationToken)
    {
        if (_scenario is not null)
        {
            string[] sensitiveValues;
            lock (_sensitiveGate) sensitiveValues = _sensitiveValues.ToArray();
            return await FakeHaEvidenceWriter
                .WriteAsync(
                    _scenario,
                    EvidenceDirectory,
                    sensitiveValues,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var path = Path.Combine(EvidenceDirectory, $"{ScenarioId}-interactions.json");
        var content = JsonSerializer.Serialize(new
        {
            scenarioId = ScenarioId,
            lifecycle = "Unavailable",
            interactions = Array.Empty<object>()
        }, JsonOptions);
        await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
        return path;
    }

    private async Task SanitizeFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(
                path,
                SanitizeJson(content),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private string SanitizeJson(string json)
    {
        var node = JsonNode.Parse(json)
                   ?? throw new JsonException("Evidence JSON was empty.");
        return SanitizeJsonNode(node)!.ToJsonString(JsonOptions);
    }

    private JsonNode? SanitizeJsonNode(JsonNode? node)
    {
        return node switch
        {
            JsonObject obj => new JsonObject(obj.Select(property =>
                KeyValuePair.Create(
                    Sanitize(property.Key),
                    SanitizeJsonNode(property.Value)))),
            JsonArray array => new JsonArray(array
                .Select(SanitizeJsonNode)
                .ToArray()),
            JsonValue value when value.TryGetValue<string>(out var text) =>
                JsonValue.Create(Sanitize(text)),
            null => null,
            _ => node.DeepClone()
        };
    }

    private string Sanitize(string value)
    {
        string[] sensitive;
        lock (_sensitiveGate) sensitive = _sensitiveValues.ToArray();

        foreach (var item in sensitive.OrderByDescending(static item => item.Length))
            value = value.Replace(item, "[REDACTED]", StringComparison.Ordinal);
        return value;
    }

    private void EnsureSanitized(params string[] paths)
    {
        string[] sensitive;
        lock (_sensitiveGate) sensitive = _sensitiveValues.ToArray();

        foreach (var path in paths)
        {
            var content = File.ReadAllText(path);
            bool containsSensitive;
            if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                using var document = JsonDocument.Parse(content);
                containsSensitive = EnumerateJsonStrings(document.RootElement)
                    .Any(value => sensitive.Any(item =>
                        value.Contains(item, StringComparison.Ordinal)));
            }
            else
            {
                containsSensitive = sensitive.Any(item =>
                    content.Contains(item, StringComparison.Ordinal));
            }
            if (containsSensitive)
            {
                throw new InvalidOperationException(
                    $"Failure evidence '{Path.GetFileName(path)}' contains a sensitive value.");
            }
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

    public void Dispose()
    {
        if (_disposed) return;
        LoggerFactory.Dispose();
        _disposed = true;
    }

    private sealed class IsolatedFileLoggerProvider(
        string path,
        Func<string, string> sanitize) : ILoggerProvider
    {
        private readonly ConcurrentDictionary<string, IsolatedFileLogger> _loggers = new();
        private readonly object _writeGate = new();

        public ILogger CreateLogger(string categoryName) =>
            _loggers.GetOrAdd(
                categoryName,
                name => new IsolatedFileLogger(name, Write));

        private void Write(string line)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            lock (_writeGate)
                File.AppendAllText(path, sanitize(line) + Environment.NewLine, Encoding.UTF8);
        }

        public void Dispose() => _loggers.Clear();

        private sealed class IsolatedFileLogger(
            string category,
            Action<string> write) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;
                var message = formatter(state, exception);
                var line = $"{DateTimeOffset.UtcNow:O} {logLevel} {category}: {message}";
                if (exception is not null) line += Environment.NewLine + exception;
                write(line);
            }
        }
    }
}
