using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using WindowsCompanion.Core.Abstractions;
using WindowsCompanion.Core.HomeAssistant;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Testing;

namespace WindowsCompanion.E2E.Tests;

public sealed class FakeHomeAssistantContractTests
{
    [Fact]
    public async Task Healthy_oauth_rest_and_webhook_sequence_matches_production_clients()
    {
        await using var scenario = await FakeHaScenario.StartAsync("healthy-http");
        using var http = new HttpClient();
        var code = await AuthorizeAsync(http, scenario);
        var oauth = new HaOAuthClient(http, scenario.BaseUrl!.AbsoluteUri);
        var tokens = await oauth.ExchangeCodeAsync(code, "http://localhost:8390/");
        var client = new HomeAssistantClient(
            http,
            scenario.BaseUrl.AbsoluteUri,
            new StaticTokenProvider(tokens.AccessToken));

        Assert.True(await client.ValidateAsync());
        var registration = await client.RegisterDeviceAsync(new DeviceRegistrationRequest
        {
            DeviceId = "contract-device",
            DeviceName = "Contract Device",
            OsVersion = "test"
        });
        Assert.Equal(scenario.WebhookId, registration.WebhookId);

        var sensor = new Sensor
        {
            UniqueId = "contract_sensor",
            Name = "Contract Sensor",
            State = 42
        };
        await client.RegisterSensorAsync(registration.WebhookId, sensor);
        await client.UpdateSensorsAsync(registration.WebhookId, [sensor]);
        var instance = await client.GetInstanceInfoAsync(registration.WebhookId);

        Assert.Equal(scenario.InstanceDeviceId, instance?.DeviceId);
        Assert.Single(scenario.State.Registrations);
        Assert.True(scenario.State.RegisteredSensors.ContainsKey(sensor.UniqueId));
        Assert.True(scenario.State.SensorStates.ContainsKey(sensor.UniqueId));
        Assert.DoesNotContain(
            scenario.Interactions.Snapshot(),
            interaction => ContainsSecret(interaction, scenario));
    }

    [Fact]
    public async Task OAuth_and_operation_faults_are_explicit_and_resettable()
    {
        await using var scenario = await FakeHaScenario.StartAsync("faulted-http");
        using var http = new HttpClient();
        var code = await AuthorizeAsync(http, scenario);
        var oauth = new HaOAuthClient(http, scenario.BaseUrl!.AbsoluteUri);

        scenario.Faults.RejectAuthorizationCode = true;
        await Assert.ThrowsAsync<HomeAssistantAuthException>(
            () => oauth.ExchangeCodeAsync(code, "http://localhost:8390/"));
        scenario.Faults.RejectAuthorizationCode = false;
        var tokens = await oauth.ExchangeCodeAsync(code, "http://localhost:8390/");

        var client = new HomeAssistantClient(
            http,
            scenario.BaseUrl.AbsoluteUri,
            new StaticTokenProvider(tokens.AccessToken));
        var registration = await client.RegisterDeviceAsync(new DeviceRegistrationRequest
        {
            DeviceId = "fault-device",
            DeviceName = "Fault Device",
            OsVersion = "test"
        });
        var sensor = new Sensor
        {
            UniqueId = "rejected_sensor",
            Name = "Rejected Sensor",
            State = "synthetic"
        };
        await client.RegisterSensorAsync(registration.WebhookId, sensor);
        scenario.Faults.RejectSensorUniqueId = sensor.UniqueId;

        var exception = await Assert.ThrowsAsync<HomeAssistantRejectedException>(
            () => client.UpdateSensorsAsync(registration.WebhookId, [sensor]));
        Assert.False(exception.SensorsUnregistered);

        scenario.Faults.UnknownWebhook = true;
        Assert.Null(await client.GetInstanceInfoAsync(registration.WebhookId));
        scenario.Faults.Reset();
        Assert.NotNull(await client.GetInstanceInfoAsync(registration.WebhookId));

        scenario.State.DeletedWebhooks.TryAdd(registration.WebhookId, 0);
        Assert.Null(await client.GetInstanceInfoAsync(registration.WebhookId));
        Assert.Contains(
            scenario.Interactions.Snapshot(),
            interaction => interaction.Outcome == "Gone");
    }

    [Fact]
    public async Task Delayed_fault_uses_an_observable_cancellable_handshake()
    {
        await using var scenario = await FakeHaScenario.StartAsync("held-api");
        using var http = new HttpClient();
        var client = new HomeAssistantClient(
            http,
            scenario.BaseUrl!.AbsoluteUri,
            new StaticTokenProvider(scenario.AccessToken));
        scenario.Faults.Hold(FakeHaFaultPoint.Api);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var validation = client.ValidateAsync(cancellation.Token);
        await scenario.Faults.WaitUntilHeldAsync(
            FakeHaFaultPoint.Api, cancellation.Token);
        Assert.False(validation.IsCompleted);

        scenario.Faults.Release(FakeHaFaultPoint.Api);
        Assert.True(await validation);

        scenario.Faults.ApiUnavailable = true;
        Assert.False(await client.ValidateAsync(cancellation.Token));
    }

    [Fact]
    public async Task Authorization_rejects_non_loopback_redirects()
    {
        await using var scenario = await FakeHaScenario.StartAsync("unsafe-redirect");
        using var http = new HttpClient();
        var authorize = HaOAuthClient.BuildAuthorizeUrl(
            scenario.BaseUrl!.AbsoluteUri,
            "https://client.example/",
            "https://redirect.example/callback",
            "unsafe-state");

        using var response = await http.GetAsync(authorize);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            scenario.Interactions.Snapshot(),
            interaction => interaction.Kind == FakeHaInteractionKind.Authorization
                           && interaction.Outcome == "Rejected");
    }

    [Fact]
    public async Task WebSocket_authentication_subscription_notification_and_confirmation_work()
    {
        await using var scenario = await FakeHaScenario.StartAsync("healthy-websocket");
        var client = new HaWebSocketClient(
            static () => new ClientWebSocketAdapter(),
            scenario.BaseUrl!.AbsoluteUri,
            new StaticTokenProvider(scenario.AccessToken),
            scenario.WebhookId);
        NotificationMessage? received = null;
        client.NotificationReceived += notification => received = notification;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = client.RunAsync(cancellation.Token);

        await scenario.Interactions.WaitForAsync(
            interaction => interaction.PathOrMessageType == "push_subscribed",
            TimeSpan.FromSeconds(5),
            cancellation.Token);
        await scenario.SendNotificationAsync(
            "Synthetic title",
            "Synthetic message",
            "confirm-contract",
            cancellation.Token);
        await scenario.Interactions.WaitForAsync(
            interaction => interaction.PathOrMessageType == "confirmation",
            TimeSpan.FromSeconds(5),
            cancellation.Token);

        await scenario.CloseWebSocketsAsync(cancellation.Token);
        await run.WaitAsync(cancellation.Token);
        Assert.Equal("Synthetic title", received?.Title);
        Assert.Equal("Synthetic message", received?.Message);
        Assert.True(scenario.State.ConfirmedNotifications.ContainsKey("confirm-contract"));
    }

    [Fact]
    public async Task WebSocket_faulted_close_allows_a_fresh_reconnection()
    {
        await using var scenario = await FakeHaScenario.StartAsync("websocket-reconnect");
        scenario.Faults.ClosePushChannelAt = FakeHaWebSocketStep.PushSubscribed;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var first = CreateSocketClient(scenario);
        await first.RunAsync(cancellation.Token);
        var boundary = scenario.Interactions.Snapshot().Last().Sequence;

        scenario.Faults.ClosePushChannelAt = null;
        var second = CreateSocketClient(scenario);
        var secondRun = second.RunAsync(cancellation.Token);
        await scenario.Interactions.WaitForAsync(
            interaction => interaction.PathOrMessageType == "push_subscribed",
            TimeSpan.FromSeconds(5),
            cancellation.Token,
            boundary);
        await scenario.CloseWebSocketsAsync(cancellation.Token);
        await secondRun.WaitAsync(cancellation.Token);

        Assert.True(scenario.Interactions.Snapshot().Count(
            interaction => interaction.PathOrMessageType == "connected") >= 2);
    }

    [Fact]
    public async Task WebSocket_rejects_an_invalid_access_token()
    {
        await using var scenario = await FakeHaScenario.StartAsync("websocket-auth-fault");
        var client = new HaWebSocketClient(
            static () => new ClientWebSocketAdapter(),
            scenario.BaseUrl!.AbsoluteUri,
            new StaticTokenProvider("synthetic-wrong-token"),
            scenario.WebhookId);

        await Assert.ThrowsAsync<HomeAssistantAuthException>(
            () => client.RunAsync(CancellationToken.None));
    }

    private static HaWebSocketClient CreateSocketClient(FakeHaScenario scenario) =>
        new(
            static () => new ClientWebSocketAdapter(),
            scenario.BaseUrl!.AbsoluteUri,
            new StaticTokenProvider(scenario.AccessToken),
            scenario.WebhookId);

    private static async Task<string> AuthorizeAsync(
        HttpClient http,
        FakeHaScenario scenario)
    {
        using var noRedirect = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false
        });
        var authorize = HaOAuthClient.BuildAuthorizeUrl(
            scenario.BaseUrl!.AbsoluteUri,
            "http://localhost:8390/",
            "http://localhost:8390/",
            "contract-state");
        using var response = await noRedirect.GetAsync(authorize);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location
                       ?? throw new InvalidOperationException("Authorization did not redirect.");
        var query = ParseQuery(location.Query);
        Assert.Equal("contract-state", query["state"]);
        return query["code"];
    }

    private static Dictionary<string, string> ParseQuery(string query) =>
        query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                part => Uri.UnescapeDataString(part[0]),
                part => Uri.UnescapeDataString(part[1]),
                StringComparer.Ordinal);

    private static bool ContainsSecret(
        FakeHaInteraction interaction,
        FakeHaScenario scenario)
    {
        var text = JsonSerializer.Serialize(interaction);
        return text.Contains(scenario.AccessToken, StringComparison.Ordinal)
               || text.Contains(scenario.RefreshToken, StringComparison.Ordinal)
               || text.Contains(scenario.WebhookId, StringComparison.Ordinal);
    }

    private sealed class StaticTokenProvider(string token) : IAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken ct = default) =>
            ValueTask.FromResult<string?>(token);
    }
}
