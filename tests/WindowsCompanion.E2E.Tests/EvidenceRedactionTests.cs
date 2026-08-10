using WindowsCompanion.Testing;

namespace WindowsCompanion.E2E.Tests;

public sealed class EvidenceRedactionTests
{
    [Fact]
    public async Task Exported_interactions_do_not_contain_scenario_secrets()
    {
        await using var scenario = await FakeHaScenario.StartAsync("evidence-redaction");
        const string suppliedInvalidCredential = "synthetic-wrong-token";
        const string suppliedInvalidCode = "synthetic-wrong-authorization-code";
        scenario.Interactions.Record(
            FakeHaInteractionKind.Token,
            "POST",
            $"/auth/token/{scenario.WebhookId}",
            new
            {
                access_token = scenario.AccessToken,
                refresh_token = scenario.RefreshToken,
                webhook_id = scenario.WebhookId
            });
        scenario.Interactions.Record(
            FakeHaInteractionKind.WebSocket,
            "CLIENT",
            "auth",
            new { access_token = suppliedInvalidCredential });
        scenario.Interactions.Record(
            FakeHaInteractionKind.Token,
            "POST",
            "authorization_code",
            new { code = suppliedInvalidCode });
        scenario.Interactions.Record(
            FakeHaInteractionKind.Token,
            "POST",
            "root-string",
            scenario.AccessToken);
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"WindowsCompanion-Evidence-{Guid.NewGuid():N}");

        try
        {
            var path = await FakeHaEvidenceWriter.WriteAsync(scenario, directory);
            var json = await File.ReadAllTextAsync(path);

            Assert.Contains("[REDACTED]", json);
            Assert.DoesNotContain(scenario.AccessToken, json, StringComparison.Ordinal);
            Assert.DoesNotContain(scenario.RefreshToken, json, StringComparison.Ordinal);
            Assert.DoesNotContain(scenario.WebhookId, json, StringComparison.Ordinal);
            Assert.DoesNotContain(suppliedInvalidCredential, json, StringComparison.Ordinal);
            Assert.DoesNotContain(suppliedInvalidCode, json, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
