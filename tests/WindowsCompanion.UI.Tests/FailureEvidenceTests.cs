using System.Text.Json;
using WindowsCompanion.UI.Tests.Fixtures;

namespace WindowsCompanion.UI.Tests;

public sealed class FailureEvidenceTests
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public async Task Induced_failure_writes_screenshot_and_sanitized_tree()
    {
        var root = EvidenceRoot();
        const string secret = "synthetic-sensitive-value";
        const string endpoint = "http://127.0.0.1:45678/";
        Directory.CreateDirectory(root);
        var appLogSource = Path.Combine(root, "source-app.log");
        await File.WriteAllTextAsync(
            appLogSource,
            $"Connection to {endpoint} failed with {secret}.");
        var evidence = new UiFailureEvidence(
            "ui-evidence-contract",
            root,
            [secret, endpoint],
            (path, cancellationToken) =>
                File.WriteAllBytesAsync(path, OnePixelPng, cancellationToken),
            () => new UiAccessibilityNode(
                "Connect.Error",
                "Text",
                $"Could not reach {endpoint} or https://example.invalid/path with {secret}",
                true,
                true,
                [
                    new UiAccessibilityNode(
                        "Status.Server",
                        "Text",
                        endpoint,
                        true,
                        true,
                        [])
                ]),
            appLogPath: appLogSource);

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                evidence.CaptureOnFailureAsync(
                    "sign-in retry",
                    () => throw new InvalidOperationException(
                        $"Induced failure for {secret} at {endpoint}")));

            Assert.Contains("Induced failure", exception.Message, StringComparison.Ordinal);
            var directory = Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                .Single(path => File.Exists(Path.Combine(path, "failure.json")));
            var screenshot = Path.Combine(directory, "screenshot.png");
            var tree = await File.ReadAllTextAsync(
                Path.Combine(directory, "accessibility-tree.json"));
            var appLog = await File.ReadAllTextAsync(Path.Combine(directory, "app.log"));
            var failure = await File.ReadAllTextAsync(Path.Combine(directory, "failure.json"));

            Assert.True(File.Exists(screenshot));
            Assert.Equal(OnePixelPng, await File.ReadAllBytesAsync(screenshot));
            Assert.Contains("[REDACTED]", tree, StringComparison.Ordinal);
            Assert.Contains("[URI]", tree, StringComparison.Ordinal);
            Assert.Contains("[REDACTED]", appLog, StringComparison.Ordinal);
            Assert.Contains("sign-in retry", failure, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, tree, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secret, failure, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(endpoint, tree, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secret, appLog, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(endpoint, appLog, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(endpoint, failure, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Capture_errors_do_not_replace_the_original_failure()
    {
        var root = EvidenceRoot();
        var evidence = new UiFailureEvidence(
            "ui-capture-error",
            root,
            [],
            (_, _) => throw new InvalidOperationException("screenshot unavailable"),
            () => throw new InvalidOperationException("tree unavailable"));

        try
        {
            var exception = await Assert.ThrowsAsync<ApplicationException>(() =>
                evidence.CaptureOnFailureAsync(
                    "failed assertion",
                    () => throw new ApplicationException("original failure")));

            Assert.Equal("original failure", exception.Message);
            var failurePath = Directory
                .EnumerateFiles(root, "failure.json", SearchOption.AllDirectories)
                .Single();
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(failurePath));
            var errors = document.RootElement.GetProperty("captureErrors");
            Assert.Equal(2, errors.GetArrayLength());
            Assert.Contains(
                errors.EnumerateArray(),
                error => error.GetString()!.StartsWith(
                    "screenshot:",
                    StringComparison.Ordinal));
            Assert.Contains(
                errors.EnumerateArray(),
                error => error.GetString()!.StartsWith(
                    "accessibility-tree:",
                    StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string EvidenceRoot() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "failure-evidence-contract",
            Guid.NewGuid().ToString("N"));
}
