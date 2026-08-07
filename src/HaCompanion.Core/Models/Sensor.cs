using System.Text.Json.Serialization;

namespace HaCompanion.Core.Models;

/// <summary>
/// A Home Assistant sensor reported via the mobile_app webhook.
/// </summary>
public sealed class Sensor
{
    [JsonPropertyName("unique_id")]
    public string UniqueId { get; set; } = string.Empty;

    /// <summary>"sensor" or "binary_sensor".</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "sensor";

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public object? State { get; set; }

    [JsonPropertyName("device_class")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeviceClass { get; set; }

    [JsonPropertyName("icon")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; set; }

    [JsonPropertyName("unit_of_measurement")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UnitOfMeasurement { get; set; }

    [JsonPropertyName("state_class")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StateClass { get; set; }

    [JsonPropertyName("entity_category")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EntityCategory { get; set; }

    [JsonPropertyName("attributes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IDictionary<string, object>? Attributes { get; set; }
}
