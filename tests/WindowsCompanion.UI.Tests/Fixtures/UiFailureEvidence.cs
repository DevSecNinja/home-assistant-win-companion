using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using WindowsCompanion.Testing;

namespace WindowsCompanion.UI.Tests.Fixtures;

internal sealed partial class UiFailureEvidence
{
    private const int MaximumTreeNodes = 5_000;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _scenarioId;
    private readonly string _evidenceRoot;
    private readonly IReadOnlyList<string> _sensitiveValues;
    private readonly Func<UiAccessibilityNode>? _captureTree;
    private readonly Func<string, CancellationToken, Task>? _writeInteractions;
    private readonly string? _appLogPath;

    internal UiFailureEvidence(
        Window? window,
        FakeHaScenario scenario,
        string evidenceRoot,
        IEnumerable<string> additionalSensitiveValues,
        string? appLogPath)
        : this(
            scenario.ScenarioId,
            evidenceRoot,
            ScenarioSensitiveValues(scenario).Concat(additionalSensitiveValues),
            window is null
                ? null
                : () => CaptureTree(window, ScenarioSensitiveValues(scenario)
                    .Concat(additionalSensitiveValues)
                    .ToArray()),
            (directory, cancellationToken) =>
                FakeHaEvidenceWriter.WriteAsync(scenario, directory, cancellationToken),
            appLogPath)
    {
    }

    internal UiFailureEvidence(
        string scenarioId,
        string evidenceRoot,
        IEnumerable<string> sensitiveValues,
        Func<UiAccessibilityNode>? captureTree,
        Func<string, CancellationToken, Task>? writeInteractions = null,
        string? appLogPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioId);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceRoot);
        _scenarioId = scenarioId;
        _evidenceRoot = evidenceRoot;
        _sensitiveValues = sensitiveValues
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        _captureTree = captureTree;
        _writeInteractions = writeInteractions;
        _appLogPath = appLogPath;
    }

    internal async Task<T> CaptureOnFailureAsync<T>(
        string step,
        Func<Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            try
            {
                await CaptureAsync(step, exception, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Failure evidence is best effort and must never replace the test failure.
            }
            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
    }

    internal Task CaptureOnFailureAsync(
        string step,
        Func<Task> action,
        CancellationToken cancellationToken = default) =>
        CaptureOnFailureAsync(
            step,
            async () =>
            {
                await action().ConfigureAwait(false);
                return true;
            },
            cancellationToken);

    internal async Task<UiFailureEvidenceResult> CaptureAsync(
        string step,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(step);
        ArgumentNullException.ThrowIfNull(exception);

        var directory = CreateEvidenceDirectory(step);
        var errors = new List<string>();
        var treePath = Path.Combine(directory, "accessibility-tree.json");
        var appLogArtifactPath = Path.Combine(directory, "app.log");
        var failurePath = Path.Combine(directory, "failure.json");

        if (_captureTree is not null)
        {
            try
            {
                var tree = SanitizeTree(_captureTree(), _sensitiveValues);
                await File.WriteAllTextAsync(
                        treePath,
                        JsonSerializer.Serialize(tree, JsonOptions),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception captureException)
            {
                treePath = string.Empty;
                errors.Add($"accessibility-tree: {Sanitize(captureException.Message, _sensitiveValues)}");
            }
        }
        else
        {
            treePath = string.Empty;
        }

        if (_writeInteractions is not null)
        {
            try
            {
                await _writeInteractions(directory, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception captureException)
            {
                errors.Add($"interactions: {Sanitize(captureException.Message, _sensitiveValues)}");
            }
        }

        if (!string.IsNullOrWhiteSpace(_appLogPath) && File.Exists(_appLogPath))
        {
            try
            {
                var appLog = await File.ReadAllTextAsync(_appLogPath, cancellationToken)
                    .ConfigureAwait(false);
                await File.WriteAllTextAsync(
                        appLogArtifactPath,
                        Sanitize(appLog, _sensitiveValues),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception captureException)
            {
                appLogArtifactPath = string.Empty;
                errors.Add($"app-log: {Sanitize(captureException.Message, _sensitiveValues)}");
            }
        }
        else
        {
            appLogArtifactPath = string.Empty;
        }

        var failure = new
        {
            scenarioId = _scenarioId,
            step = Sanitize(step, _sensitiveValues),
            exceptionType = exception.GetType().FullName,
            message = Sanitize(exception.Message, _sensitiveValues),
            capturedAt = DateTimeOffset.UtcNow,
            captureErrors = errors
        };
        await File.WriteAllTextAsync(
                failurePath,
                JsonSerializer.Serialize(failure, JsonOptions),
                cancellationToken)
            .ConfigureAwait(false);

        return new UiFailureEvidenceResult(
            directory,
            string.IsNullOrEmpty(treePath) ? null : treePath,
            string.IsNullOrEmpty(appLogArtifactPath) ? null : appLogArtifactPath,
            failurePath,
            errors);
    }

    internal static string Sanitize(string value, IEnumerable<string> sensitiveValues)
    {
        var sanitized = value;
        foreach (var sensitiveValue in sensitiveValues
                     .Where(static candidate => !string.IsNullOrWhiteSpace(candidate))
                     .OrderByDescending(static candidate => candidate.Length))
        {
            sanitized = sanitized.Replace(
                sensitiveValue,
                "[REDACTED]",
                StringComparison.OrdinalIgnoreCase);
        }

        return UriPattern().Replace(sanitized, "[URI]");
    }

    private string CreateEvidenceDirectory(string step)
    {
        var scenario = SafeFilePart(_scenarioId);
        var stepName = SafeFilePart(Sanitize(step, _sensitiveValues));
        var run = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}"[..37];
        var directory = Path.Combine(_evidenceRoot, scenario, $"{run}-{stepName}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static UiAccessibilityNode CaptureTree(
        AutomationElement root,
        IReadOnlyList<string> sensitiveValues)
    {
        var remaining = MaximumTreeNodes;
        return CaptureNode(root, sensitiveValues, 0, ref remaining);
    }

    private static UiAccessibilityNode CaptureNode(
        AutomationElement element,
        IReadOnlyList<string> sensitiveValues,
        int depth,
        ref int remaining)
    {
        remaining--;
        var node = new UiAccessibilityNode(
            Sanitize(Safe(() => element.AutomationId, string.Empty), sensitiveValues),
            Sanitize(Safe(() => element.ControlType.ToString(), "Unknown"), sensitiveValues),
            Sanitize(Safe(() => element.Name, string.Empty), sensitiveValues),
            Safe(() => element.IsEnabled, false),
            !Safe(() => element.IsOffscreen, true),
            []);
        if (remaining <= 0 || depth >= 64) return node;

        AutomationElement[] children;
        try
        {
            children = element.FindAllChildren();
        }
        catch
        {
            return node;
        }

        foreach (var child in children)
        {
            if (remaining <= 0) break;
            node.Children.Add(CaptureNode(child, sensitiveValues, depth + 1, ref remaining));
        }

        return node;
    }

    private static UiAccessibilityNode SanitizeTree(
        UiAccessibilityNode node,
        IReadOnlyList<string> sensitiveValues) =>
        new(
            Sanitize(node.AutomationId, sensitiveValues),
            Sanitize(node.ControlType, sensitiveValues),
            Sanitize(node.Name, sensitiveValues),
            node.IsEnabled,
            node.IsVisible,
            node.Children.Select(child => SanitizeTree(child, sensitiveValues)).ToList());

    private static T Safe<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch
        {
            return fallback;
        }
    }

    private static IEnumerable<string> ScenarioSensitiveValues(FakeHaScenario scenario)
    {
        yield return scenario.AccessToken;
        yield return scenario.RefreshToken;
        yield return scenario.WebhookId;
        if (scenario.BaseUrl is not null)
        {
            yield return scenario.BaseUrl.AbsoluteUri;
            yield return scenario.BaseUrl.AbsoluteUri.TrimEnd('/');
        }
    }

    private static string SafeFilePart(string value)
    {
        var safe = InvalidFilePart().Replace(value, "-").Trim('-', '.', ' ');
        return string.IsNullOrEmpty(safe) ? "failure" : safe[..Math.Min(safe.Length, 80)];
    }

    [GeneratedRegex(@"https?://[^\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UriPattern();

    [GeneratedRegex(@"[^A-Za-z0-9._-]+", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidFilePart();
}

internal sealed record UiFailureEvidenceResult(
    string Directory,
    string? AccessibilityTreePath,
    string? AppLogPath,
    string FailurePath,
    IReadOnlyList<string> CaptureErrors);

internal sealed record UiAccessibilityNode(
    string AutomationId,
    string ControlType,
    string Name,
    bool IsEnabled,
    bool IsVisible,
    List<UiAccessibilityNode> Children);
