using System.Net;
using System.Text;
using System.Text.Json;
using HaCompanion.Core.HomeAssistant;
using HaCompanion.Core.Models;
using Xunit;

namespace HaCompanion.Core.Tests;

public class HomeAssistantClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public readonly List<HttpRequestMessage> Requests = new();
        public readonly List<string> Bodies = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct));
            return _responder(request);
        }
    }

    private static HomeAssistantClient CreateClient(StubHandler handler, string token = "tok-1234")
        => new(new HttpClient(handler), "https://ha.local:8123/", () => token);

    [Fact]
    public async Task ValidateAsync_sends_bearer_token_and_returns_true_on_success()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(handler);

        var ok = await client.ValidateAsync();

        Assert.True(ok);
        var req = Assert.Single(handler.Requests);
        Assert.Equal("https://ha.local:8123/api/", req.RequestUri!.ToString());
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
        Assert.Equal("tok-1234", req.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task ValidateAsync_throws_auth_exception_on_401()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<HomeAssistantAuthException>(() => client.ValidateAsync());
    }

    [Fact]
    public async Task RegisterDeviceAsync_posts_to_registrations_and_parses_response()
    {
        var json = """{"webhook_id":"wh-1","secret":null,"cloudhook_url":null,"remote_ui_url":null}""";
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        var client = CreateClient(handler);

        var result = await client.RegisterDeviceAsync(new DeviceRegistrationRequest { DeviceId = "dev-1" });

        Assert.Equal("wh-1", result.WebhookId);
        var req = Assert.Single(handler.Requests);
        Assert.Equal("https://ha.local:8123/api/mobile_app/registrations", req.RequestUri!.ToString());
        Assert.Contains("\"device_id\":\"dev-1\"", handler.Bodies[0]);
        Assert.Contains("\"supports_encryption\":false", handler.Bodies[0]);
    }

    [Fact]
    public async Task RegisterDeviceAsync_throws_when_mobile_app_not_enabled()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.RegisterDeviceAsync(new DeviceRegistrationRequest { DeviceId = "dev-1" }));
    }

    [Fact]
    public async Task UpdateSensorsAsync_posts_batch_to_webhook_url()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(handler);

        await client.UpdateSensorsAsync("wh-1", new[]
        {
            new Sensor { UniqueId = "battery_level", State = 55 }
        });

        var req = Assert.Single(handler.Requests);
        Assert.Equal("https://ha.local:8123/api/webhook/wh-1", req.RequestUri!.ToString());
        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal("update_sensor_states", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("battery_level", doc.RootElement.GetProperty("data")[0].GetProperty("unique_id").GetString());
    }
}
