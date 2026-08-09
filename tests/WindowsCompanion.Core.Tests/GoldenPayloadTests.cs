using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using WindowsCompanion.Core.HomeAssistant;
using WindowsCompanion.Core.Models;

namespace WindowsCompanion.Core.Tests;

public class GoldenPayloadTests
{
    [Fact]
    public async Task Device_registration_matches_the_verified_contract()
    {
        var (client, handler) = CreateClient(
            """{"webhook_id":"hook","secret":null,"cloudhook_url":null,"remote_ui_url":null}""");

        await client.RegisterDeviceAsync(Registration());

        AssertMatchesGolden("device-registration.json", Assert.Single(handler.Bodies));
    }

    [Fact]
    public async Task Registration_update_matches_the_verified_contract()
    {
        var (client, handler) = CreateClient("{}");

        await client.UpdateRegistrationAsync("hook", Registration());

        AssertMatchesGolden("update-registration.json", Assert.Single(handler.Bodies));
    }

    [Fact]
    public async Task Disabled_sensor_registration_matches_the_verified_contract()
    {
        var (client, handler) = CreateClient("{}");

        await client.RegisterSensorAsync("hook", new Sensor
        {
            UniqueId = "microphone",
            Type = "binary_sensor",
            Name = "Microphone In Use",
            State = false,
            Icon = "mdi:microphone-off",
            Disabled = true
        });

        AssertMatchesGolden("register-disabled-sensor.json", Assert.Single(handler.Bodies));
    }

    [Fact]
    public async Task Sensor_state_update_matches_the_verified_contract()
    {
        var (client, handler) = CreateClient("{}");

        await client.UpdateSensorsAsync("hook",
        [
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
        ]);

        AssertMatchesGolden("update-sensor-states.json", Assert.Single(handler.Bodies));
    }

    private static DeviceRegistrationRequest Registration() => new()
    {
        DeviceId = "device-123",
        AppVersion = "1.2.3",
        DeviceName = "DESKTOP-TEST",
        Manufacturer = "Contoso",
        Model = "Windows PC",
        OsVersion = "10.0.26100"
    };

    private static (HomeAssistantClient Client, CaptureHandler Handler) CreateClient(string response)
    {
        var handler = new CaptureHandler(response);
        var client = new HomeAssistantClient(
            new HttpClient(handler),
            "https://ha.local:8123/",
            new StaticTokenProvider("token"));
        return (client, handler);
    }

    private static void AssertMatchesGolden(string fileName, string actualJson)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Golden", fileName);
        var expected = JsonNode.Parse(File.ReadAllText(path));
        var actual = JsonNode.Parse(actualJson);

        Assert.True(
            JsonNode.DeepEquals(expected, actual),
            $"Payload did not match {fileName}.{Environment.NewLine}"
            + $"Expected: {expected}{Environment.NewLine}Actual: {actual}");
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly string _response;
        public List<string> Bodies { get; } = [];

        public CaptureHandler(string response) => _response = response;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response, Encoding.UTF8, "application/json")
            };
        }
    }
}
