using System.Text.Json.Serialization;

namespace WindowsCompanion.Core.Models;

/// <summary>
/// What the <c>get_config</c> webhook reports about the instance behind an
/// address. Returned by Home Assistant only for a webhook it actually knows, so
/// receiving one is itself evidence that this registration lives there.
/// </summary>
public sealed class HaInstanceInfo
{
    /// <summary>
    /// Home Assistant's device-registry id for this registration. Stable across
    /// addresses and restarts, and different on any other instance, which makes it
    /// the identity check that matching names or versions cannot provide.
    /// </summary>
    [JsonPropertyName("hass_device_id")]
    public string? DeviceId { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>
    /// Home Assistant Cloud remote UI address, offered as a suggestion for the
    /// external route. Never enabled without the user confirming it.
    /// </summary>
    [JsonPropertyName("remote_ui_url")]
    public string? RemoteUiUrl { get; set; }

    /// <summary>Present only on cloud installs; embeds the webhook capability secret.</summary>
    [JsonPropertyName("cloudhook_url")]
    public string? CloudhookUrl { get; set; }
}

/// <summary>
/// The instance's own view of how it should be reached, from <c>GET /api/config</c>.
/// Used purely to suggest addresses the user can accept or ignore.
/// </summary>
public sealed class HaConfigInfo
{
    [JsonPropertyName("internal_url")]
    public string? InternalUrl { get; set; }

    [JsonPropertyName("external_url")]
    public string? ExternalUrl { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}
