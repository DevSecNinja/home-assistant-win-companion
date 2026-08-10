using System.Text.RegularExpressions;

namespace WindowsCompanion.Testing;

/// <summary>Describes the lifecycle of an isolated fake Home Assistant scenario.</summary>
public enum FakeHaScenarioLifecycle
{
    /// <summary>The scenario has been created but not started.</summary>
    Created,
    /// <summary>The fake server is starting.</summary>
    Starting,
    /// <summary>The fake server is accepting requests.</summary>
    Running,
    /// <summary>The scenario is stopping.</summary>
    Stopping,
    /// <summary>The scenario and server have been disposed.</summary>
    Disposed
}

/// <summary>Owns an isolated fake Home Assistant server, state, faults, and interactions.</summary>
public sealed partial class FakeHaScenario : IAsyncDisposable
{
    private FakeHomeAssistantServer? _server;

    /// <summary>Creates a scenario without starting its server.</summary>
    public FakeHaScenario(string? scenarioId = null)
    {
        ScenarioId = scenarioId ?? $"scenario-{Guid.NewGuid():N}";
        if (!SafeScenarioId().IsMatch(ScenarioId))
            throw new ArgumentException(
                "Scenario id may contain only letters, numbers, dot, underscore, and hyphen.",
                nameof(scenarioId));

        var suffix = Guid.NewGuid().ToString("N");
        InstanceDeviceId = $"test-device-{suffix}";
        AccessToken = $"test-access-{suffix}";
        RefreshToken = $"test-refresh-{suffix}";
        WebhookId = $"test-webhook-{suffix}";
        AuthorizationCode = $"test-code-{suffix}";
        Interactions = new FakeHaInteractionLog(SensitiveValues);
    }

    /// <summary>Gets the filesystem-safe scenario identifier.</summary>
    public string ScenarioId { get; }
    /// <summary>Gets the loopback server address after startup.</summary>
    public Uri? BaseUrl { get; internal set; }
    /// <summary>Gets the synthetic Home Assistant instance device identifier.</summary>
    public string InstanceDeviceId { get; }
    /// <summary>Gets the scenario access token. Evidence writers redact this value.</summary>
    public string AccessToken { get; }
    /// <summary>Gets the scenario refresh token. Evidence writers redact this value.</summary>
    public string RefreshToken { get; }
    /// <summary>Gets the scenario webhook identifier. Evidence writers redact this value.</summary>
    public string WebhookId { get; }
    internal string AuthorizationCode { get; }
    /// <summary>Gets the server-observed scenario state.</summary>
    public FakeHaState State { get; } = new();
    /// <summary>Gets the deterministic fault controls.</summary>
    public FakeHaFaults Faults { get; } = new();
    /// <summary>Gets the sanitized interaction log.</summary>
    public FakeHaInteractionLog Interactions { get; }
    /// <summary>Gets the current scenario lifecycle state.</summary>
    public FakeHaScenarioLifecycle Lifecycle { get; internal set; } = FakeHaScenarioLifecycle.Created;

    /// <summary>Creates and starts an isolated loopback scenario.</summary>
    public static async Task<FakeHaScenario> StartAsync(
        string? scenarioId = null,
        CancellationToken cancellationToken = default)
    {
        var scenario = new FakeHaScenario(scenarioId);
        try
        {
            scenario.Lifecycle = FakeHaScenarioLifecycle.Starting;
            scenario._server = await FakeHomeAssistantServer
                .StartAsync(scenario, cancellationToken)
                .ConfigureAwait(false);
            scenario.Lifecycle = FakeHaScenarioLifecycle.Running;
            return scenario;
        }
        catch
        {
            await scenario.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Sends a notification to each subscribed WebSocket client.</summary>
    public Task SendNotificationAsync(
        string title,
        string message,
        string? confirmationId = null,
        CancellationToken cancellationToken = default) =>
        _server?.SendNotificationAsync(title, message, confirmationId, cancellationToken)
        ?? throw new InvalidOperationException("The scenario is not running.");

    /// <summary>Closes all active scenario WebSocket connections.</summary>
    public Task CloseWebSocketsAsync(
        CancellationToken cancellationToken = default) =>
        _server?.CloseWebSocketsAsync(cancellationToken)
        ?? Task.CompletedTask;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Lifecycle == FakeHaScenarioLifecycle.Disposed) return;
        Lifecycle = FakeHaScenarioLifecycle.Stopping;
        Faults.Dispose();
        if (_server is not null)
        {
            await _server.DisposeAsync().ConfigureAwait(false);
            _server = null;
        }

        Lifecycle = FakeHaScenarioLifecycle.Disposed;
    }

    private IReadOnlyCollection<string> SensitiveValues() =>
        [AccessToken, RefreshToken, WebhookId, AuthorizationCode];

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeScenarioId();
}
