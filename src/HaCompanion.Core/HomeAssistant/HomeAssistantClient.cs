using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HaCompanion.Core.Abstractions;
using HaCompanion.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HaCompanion.Core.HomeAssistant;

/// <summary>
/// Thrown when Home Assistant rejects our access token (HTTP 401).
/// </summary>
public sealed class HomeAssistantAuthException : Exception
{
    public HomeAssistantAuthException(string message) : base(message) { }
}

/// <summary>
/// Home Assistant REST + webhook client. The access token is supplied via a
/// factory so it can be rotated without rebuilding the client, and is never logged.
/// </summary>
public sealed class HomeAssistantClient : IHomeAssistantClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private readonly IAccessTokenProvider _tokens;
    private readonly ILogger<HomeAssistantClient> _log;

    public HomeAssistantClient(
        HttpClient http,
        string baseUrl,
        IAccessTokenProvider tokens,
        ILogger<HomeAssistantClient>? log = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            throw new ArgumentException("Base URL must be an absolute URI.", nameof(baseUrl));
        _baseUri = uri;
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _log = log ?? NullLogger<HomeAssistantClient>.Instance;
    }

    private async Task<HttpRequestMessage> AuthorizedAsync(HttpMethod method, string relative, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, new Uri(_baseUri, relative));
        var token = await _tokens.GetAccessTokenAsync(ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    public async Task<bool> ValidateAsync(CancellationToken ct = default)
    {
        using var request = await AuthorizedAsync(HttpMethod.Get, "api/", ct).ConfigureAwait(false);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new HomeAssistantAuthException("Home Assistant rejected the access token.");
        return response.IsSuccessStatusCode;
    }

    public async Task<DeviceRegistrationResponse> RegisterDeviceAsync(
        DeviceRegistrationRequest req, CancellationToken ct = default)
    {
        using var request = await AuthorizedAsync(HttpMethod.Post, "api/mobile_app/registrations", ct).ConfigureAwait(false);
        request.Content = JsonContent.Create(req, options: Json);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new HomeAssistantAuthException("Home Assistant rejected the access token during registration.");
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException(
                "The mobile_app integration is not enabled on this Home Assistant instance.");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<DeviceRegistrationResponse>(Json, ct).ConfigureAwait(false)
                     ?? throw new InvalidOperationException("Empty registration response from Home Assistant.");
        _log.LogInformation("Device registered with Home Assistant (webhook acquired).");
        return result;
    }

    public async Task UpdateRegistrationAsync(
        string webhookId, DeviceRegistrationRequest req, CancellationToken ct = default)
    {
        // app_version, device_name, manufacturer and model are all required by
        // Home Assistant's update_registration schema; omitting any of them makes
        // the whole call fail validation.
        var payload = new
        {
            type = "update_registration",
            data = new
            {
                app_data = req.AppData,
                app_version = req.AppVersion,
                device_name = req.DeviceName,
                manufacturer = req.Manufacturer,
                model = req.Model,
                os_version = req.OsVersion
            }
        };
        await PostWebhookAsync(webhookId, payload, ct).ConfigureAwait(false);
        _log.LogInformation("Updated device registration (local push declared).");
    }

    public async Task RegisterSensorAsync(string webhookId, Sensor sensor, CancellationToken ct = default)
    {
        var payload = new { type = "register_sensor", data = sensor };
        await PostWebhookAsync(webhookId, payload, ct).ConfigureAwait(false);
        _log.LogInformation("Registered sensor {UniqueId}.", sensor.UniqueId);
    }

    public async Task UpdateSensorsAsync(string webhookId, IReadOnlyList<Sensor> sensors, CancellationToken ct = default)
    {
        var payload = new { type = "update_sensor_states", data = sensors };
        await PostWebhookAsync(webhookId, payload, ct).ConfigureAwait(false);
    }

    private async Task PostWebhookAsync(string webhookId, object payload, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, $"api/webhook/{webhookId}"))
        {
            Content = JsonContent.Create(payload, options: Json)
        };
        using (request)
        using (var response = await _http.SendAsync(request, ct).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
        }
    }
}
