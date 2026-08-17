using System.Net;
using System.Text;
using System.Text.Json;
using WindowsCompanion.Core.HomeAssistant;
using WindowsCompanion.Core.Models;
using Xunit;

namespace WindowsCompanion.Core.Tests;

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
        => new(new HttpClient(handler), "https://ha.local:8123/", new StaticTokenProvider(token));

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
    public async Task UpdateRegistrationAsync_sends_all_fields_required_by_home_assistant()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        var client = CreateClient(handler);

        await client.UpdateRegistrationAsync("wh-9", new DeviceRegistrationRequest
        {
            DeviceId = "dev",
            AppVersion = "1.2.3",
            DeviceName = "PC-1",
            Manufacturer = "PC",
            Model = "Windows PC",
            OsVersion = "10.0"
        });

        var req = Assert.Single(handler.Requests);
        Assert.Equal("https://ha.local:8123/api/webhook/wh-9", req.RequestUri!.ToString());

        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal("update_registration", doc.RootElement.GetProperty("type").GetString());
        var data = doc.RootElement.GetProperty("data");

        // HA's update_registration schema requires these; omitting any one makes
        // the entire call fail validation and silently disables notifications.
        Assert.Equal("1.2.3", data.GetProperty("app_version").GetString());
        Assert.Equal("PC-1", data.GetProperty("device_name").GetString());
        Assert.Equal("PC", data.GetProperty("manufacturer").GetString());
        Assert.Equal("Windows PC", data.GetProperty("model").GetString());

        // Declaring the websocket push channel is what makes the PC a notify target.
        Assert.True(data.GetProperty("app_data").GetProperty("push_websocket_channel").GetBoolean());
    }

    [Fact]
    public async Task ValidateAsync_throws_auth_exception_on_401()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<HomeAssistantAuthException>(() => client.ValidateAsync());
    }

    [Fact]
    public async Task UpdateSensorsAsync_sends_only_the_keys_home_assistant_accepts()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        var client = CreateClient(handler);

        await client.UpdateSensorsAsync("wh-1", new[]
        {
            new Sensor
            {
                UniqueId = "battery_level",
                Type = "sensor",
                Name = "Battery Level",
                State = 42,
                DeviceClass = "battery",
                UnitOfMeasurement = "%",
                StateClass = "measurement",
                EntityCategory = "diagnostic",
                Icon = "mdi:battery"
            }
        });

        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        var sensor = doc.RootElement.GetProperty("data")[0];

        // HA's SENSOR_SCHEMA_FULL rejects anything else with invalid_format, which
        // silently drops the sensor from the update while still returning HTTP 200.
        var keys = sensor.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "icon", "state", "type", "unique_id" }, keys);

        Assert.Equal("battery_level", sensor.GetProperty("unique_id").GetString());
        Assert.Equal(42, sensor.GetProperty("state").GetInt32());
    }

    [Fact]
    public async Task UpdateSensorsAsync_throws_when_home_assistant_rejects_a_sensor()
    {
        // HA answers 200 even when it rejects sensors, so only the body reveals it.
        var rejection = """
            {"battery_level":{"success":false,"error":{"code":"invalid_format","message":"extra keys not allowed"}}}
            """;
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(rejection, Encoding.UTF8, "application/json")
        });
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<HomeAssistantRejectedException>(() =>
            client.UpdateSensorsAsync("wh-1", new[]
            {
                new Sensor { UniqueId = "battery_level", Type = "sensor", State = 1 }
            }));
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

    [Fact]
    public async Task GetInstanceInfoAsync_reads_the_device_id_that_proves_the_instance()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"hass_device_id":"dev-9","version":"2025.1.0","remote_ui_url":"https://x.ui.nabu.casa"}""",
                Encoding.UTF8,
                "application/json")
        });
        var client = CreateClient(handler);

        var info = await client.GetInstanceInfoAsync("wh-1");

        Assert.Equal("dev-9", info!.DeviceId);
        Assert.Equal("2025.1.0", info.Version);
        Assert.Equal("https://x.ui.nabu.casa", info.RemoteUiUrl);
        var req = Assert.Single(handler.Requests);
        Assert.Equal("https://ha.local:8123/api/webhook/wh-1", req.RequestUri!.ToString());
        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal("get_config", doc.RootElement.GetProperty("type").GetString());
        // The identity check must not carry the access token to an unproven host.
        Assert.Null(req.Headers.Authorization);
    }

    [Fact]
    public async Task GetInstanceInfoAsync_treats_an_empty_body_as_an_unknown_registration()
    {
        // Home Assistant answers 200 with no body for a webhook it does not know,
        // so that an attacker cannot enumerate valid webhook ids.
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Empty)
        });

        Assert.Null(await CreateClient(handler).GetInstanceInfoAsync("wh-1"));
    }

    [Fact]
    public async Task GetInstanceInfoAsync_treats_a_deleted_registration_as_unknown()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Gone));

        Assert.Null(await CreateClient(handler).GetInstanceInfoAsync("wh-1"));
    }

    [Fact]
    public async Task GetInstanceInfoAsync_ignores_a_response_without_a_device_id()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"version":"2025.1.0"}""", Encoding.UTF8, "application/json")
        });

        Assert.Null(await CreateClient(handler).GetInstanceInfoAsync("wh-1"));
    }

    [Fact]
    public async Task GetInstanceInfoAsync_survives_a_body_that_is_not_json()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>portal</html>", Encoding.UTF8, "text/html")
        });

        Assert.Null(await CreateClient(handler).GetInstanceInfoAsync("wh-1"));
    }

    [Fact]
    public async Task GetConfigAsync_reads_the_addresses_home_assistant_suggests()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"internal_url":"http://ha.local:8123","external_url":"https://ha.example.com","version":"2025.1.0"}""",
                Encoding.UTF8,
                "application/json")
        });

        var config = await CreateClient(handler).GetConfigAsync();

        Assert.Equal("http://ha.local:8123", config!.InternalUrl);
        Assert.Equal("https://ha.example.com", config.ExternalUrl);
        var req = Assert.Single(handler.Requests);
        Assert.Equal("https://ha.local:8123/api/config", req.RequestUri!.ToString());
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task GetConfigAsync_reports_a_rejected_token_rather_than_guessing()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<HomeAssistantAuthException>(
            () => CreateClient(handler).GetConfigAsync());
    }

    [Fact]
    public async Task GetConfigAsync_returns_nothing_when_the_endpoint_is_unavailable()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Null(await CreateClient(handler).GetConfigAsync());
    }

    [Fact]
    public async Task UpdateLocationAsync_sends_gps_array_and_accuracy_when_fix_available()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        var client = CreateClient(handler);

        await client.UpdateLocationAsync("wh-1",
            new LocationUpdate(47.398, 8.5451, 12));

        var req = Assert.Single(handler.Requests);
        Assert.Equal("https://ha.local:8123/api/webhook/wh-1", req.RequestUri!.ToString());

        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal("update_location", doc.RootElement.GetProperty("type").GetString());
        var data = doc.RootElement.GetProperty("data");
        var gps = data.GetProperty("gps");
        Assert.Equal(47.398, gps[0].GetDouble());
        Assert.Equal(8.5451, gps[1].GetDouble());
        Assert.Equal(12, data.GetProperty("gps_accuracy").GetInt32());
    }

    [Fact]
    public async Task UpdateLocationAsync_sends_location_name_when_no_fix()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        var client = CreateClient(handler);

        await client.UpdateLocationAsync("wh-1",
            new LocationUpdate(null, null, null, "not_home"));

        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal("update_location", doc.RootElement.GetProperty("type").GetString());
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal("not_home", data.GetProperty("location_name").GetString());
        Assert.False(data.TryGetProperty("gps", out _));
    }
}
