using System.Text.Json.Serialization;

namespace WindowsCompanion.Core.Models;

/// <summary>Payload sent to POST /api/mobile_app/registrations.</summary>
public sealed class DeviceRegistrationRequest
{
    [JsonPropertyName("device_id")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("app_id")]
    public string AppId { get; set; } = "io.homeassistant.windows";

    [JsonPropertyName("app_name")]
    public string AppName { get; set; } = "Windows Companion for Home Assistant";

    [JsonPropertyName("app_version")]
    public string AppVersion { get; set; } = "0.1.0";

    [JsonPropertyName("device_name")]
    public string DeviceName { get; set; } = string.Empty;

    [JsonPropertyName("manufacturer")]
    public string Manufacturer { get; set; } = "PC";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "Windows PC";

    [JsonPropertyName("os_name")]
    public string OsName { get; set; } = "Windows";

    [JsonPropertyName("os_version")]
    public string OsVersion { get; set; } = string.Empty;

    [JsonPropertyName("supports_encryption")]
    public bool SupportsEncryption { get; set; }

    /// <summary>
    /// Declares <c>push_websocket_channel</c> so Home Assistant treats this PC as
    /// push-capable and exposes it as a notify target. Notifications are then
    /// delivered over our authenticated WebSocket (local push), which is what
    /// Windows uses instead of APNS/FCM.
    /// </summary>
    [JsonPropertyName("app_data")]
    public Dictionary<string, object> AppData { get; set; } = new()
    {
        ["push_websocket_channel"] = true
    };
}

/// <summary>Response from POST /api/mobile_app/registrations.</summary>
public sealed class DeviceRegistrationResponse
{
    [JsonPropertyName("webhook_id")]
    public string WebhookId { get; set; } = string.Empty;

    [JsonPropertyName("secret")]
    public string? Secret { get; set; }

    [JsonPropertyName("cloudhook_url")]
    public string? CloudhookUrl { get; set; }

    [JsonPropertyName("remote_ui_url")]
    public string? RemoteUiUrl { get; set; }
}
