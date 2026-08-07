using System.Text.Json.Serialization;

namespace HaCompanion.Core.Models;

/// <summary>
/// Non-secret configuration describing the connected Home Assistant instance.
/// Persisted to settings.json. Secrets are stored separately in the platform secret
/// store and never serialized here.
/// </summary>
public sealed class ServerConfig
{
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Stable identifier for this installation, used as HA device_id.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// Webhook id returned by device registration; null until registered.
    /// </summary>
    /// <remarks>
    /// A capability secret, not an identifier: anyone holding it can post sensor data
    /// and open the push notification channel to receive this user's Home Assistant
    /// notifications. Home Assistant treats it the same way - its own
    /// <c>safe_registration</c> strips it. So it lives in the platform secret store
    /// and is deliberately never written to settings.json.
    /// </remarks>
    [JsonIgnore]
    public string? WebhookId { get; set; }

    /// <summary>Cloudhook URL embeds the webhook id, so it is equally sensitive.</summary>
    [JsonIgnore]
    public string? CloudhookUrl { get; set; }

    /// <summary>Not sensitive: the instance URL, which we already store in the clear.</summary>
    public string? RemoteUiUrl { get; set; }

    /// <summary>
    /// Per-sensor enablement and settings. Non-secret, so it lives here alongside
    /// the rest of the configuration.
    /// </summary>
    public Sensors.SensorPreferences Sensors { get; set; } = new();

    [JsonIgnore]
    public bool Registered => !string.IsNullOrEmpty(WebhookId);

    /// <summary>
    /// Reads the webhook id from installs that predate secret storage, so it can be
    /// migrated. Only ever populated by deserialization; cleared once migrated.
    /// </summary>
    /// <remarks>
    /// Without this, an existing install would look unregistered after upgrading and
    /// would register again, creating a duplicate device in Home Assistant.
    /// </remarks>
    [JsonPropertyName("WebhookId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyWebhookId { get; set; }

    [JsonPropertyName("CloudhookUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyCloudhookUrl { get; set; }

    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl)) return false;
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }
}
