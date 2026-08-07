namespace HaCompanion.Core.Models;

/// <summary>
/// Non-secret configuration describing the connected Home Assistant instance.
/// Persisted to settings.json. Secrets (tokens) are stored separately in the
/// platform secret store and never serialized here.
/// </summary>
public sealed class ServerConfig
{
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Stable identifier for this installation, used as HA device_id.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Webhook id returned by device registration; null until registered.</summary>
    public string? WebhookId { get; set; }

    public string? RemoteUiUrl { get; set; }

    public string? CloudhookUrl { get; set; }

    /// <summary>
    /// Per-sensor enablement and settings. Non-secret, so it lives here alongside
    /// the rest of the configuration.
    /// </summary>
    public Sensors.SensorPreferences Sensors { get; set; } = new();

    public bool Registered => !string.IsNullOrEmpty(WebhookId);

    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl)) return false;
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }
}
