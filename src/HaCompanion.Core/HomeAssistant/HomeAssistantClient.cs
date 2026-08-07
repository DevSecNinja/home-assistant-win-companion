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
/// Thrown when Home Assistant accepts the request (HTTP 200) but rejects one or
/// more individual sensors in the response body. Surfacing this as an error is
/// deliberate: a silent partial rejection previously meant sensors stopped
/// updating for hours while the app still reported itself healthy.
/// </summary>
public sealed class HomeAssistantRejectedException : Exception
{
    public HomeAssistantRejectedException(string message) : base(message) { }
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
        var payload = new { type = "update_sensor_states", data = sensors.Select(ToUpdatePayload).ToList() };
        var body = await PostWebhookAsync(webhookId, payload, ct).ConfigureAwait(false);

        // Home Assistant reports per-sensor success in the body and still returns
        // HTTP 200 when an individual sensor is rejected, so a 200 alone does not
        // mean the update landed.
        if (!string.IsNullOrWhiteSpace(body) && body.Contains("\"success\":false", StringComparison.OrdinalIgnoreCase))
        {
            _log.LogWarning("Home Assistant rejected part of a sensor update: {Body}", body);
            throw new HomeAssistantRejectedException(
                "Home Assistant rejected one or more sensor updates: " + body);
        }

        _log.LogDebug("Sensor update accepted ({Count} sensors).", sensors.Count);
    }

    /// <summary>
    /// Projects a sensor onto the only keys <c>update_sensor_states</c> accepts
    /// (HA's <c>SENSOR_SCHEMA_FULL</c>: unique_id, type, state, icon, attributes).
    /// Registration metadata such as name, device_class, entity_category,
    /// unit_of_measurement or state_class is rejected with <c>invalid_format</c>
    /// and would silently drop the whole sensor from the update.
    /// </summary>
    internal static Dictionary<string, object?> ToUpdatePayload(Sensor sensor)
    {
        var payload = new Dictionary<string, object?>
        {
            ["unique_id"] = sensor.UniqueId,
            ["type"] = sensor.Type,
            ["state"] = sensor.State
        };

        if (sensor.Icon is not null) payload["icon"] = sensor.Icon;
        if (sensor.Attributes is not null) payload["attributes"] = sensor.Attributes;

        return payload;
    }

    private async Task<string> PostWebhookAsync(string webhookId, object payload, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, $"api/webhook/{webhookId}"))
        {
            Content = JsonContent.Create(payload, options: Json)
        };
        using (request)
        using (var response = await _http.SendAsync(request, ct).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
    }
}
