using System.Text.Json.Serialization;

namespace WindowsCompanion.Core.Models;

/// <summary>
/// Non-secret configuration describing the connected Home Assistant instance.
/// Persisted to settings.json. Secrets are stored separately in the platform secret
/// store and never serialized here.
/// </summary>
public sealed class ServerConfig
{
    /// <summary>
    /// The address currently in use. Always kept in step with the active route so
    /// installs that predate dual URLs - and everything that only needs "where is
    /// Home Assistant right now" - keep working unchanged.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Address reachable on networks the user has configured as internal.</summary>
    public string? InternalUrl { get; set; }

    /// <summary>Address used from anywhere else. HTTPS only.</summary>
    public string? ExternalUrl { get; set; }

    /// <summary>
    /// Whether the user opted into separate internal/external routing. The default
    /// is one address in <see cref="BaseUrl"/>.
    /// </summary>
    public bool UseSeparateUrls { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ConnectionMode ConnectionMode { get; set; } = ConnectionMode.Automatic;

    /// <summary>Local-only rules that make the internal address eligible.</summary>
    public TrustedNetworkSettings TrustedNetworks { get; set; } = new();

    /// <summary>The route that last carried a validated connection.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RouteKind? LastSuccessfulRoute { get; set; }

    public DateTimeOffset? LastSuccessfulRouteAt { get; set; }

    /// <summary>
    /// Home Assistant's own device-registry id for this installation, read from
    /// the <c>get_config</c> webhook. Both addresses must report the same value,
    /// which is what proves they are the same instance rather than two servers
    /// that happen to share a name or version.
    /// </summary>
    /// <remarks>
    /// Not a credential: it identifies a device row inside the user's own Home
    /// Assistant and grants nothing on its own.
    /// </remarks>
    public string? InstanceDeviceId { get; set; }

    /// <summary>
    /// Retained for compatibility with the first dual-URL release. Current
    /// versions migrate this state back to the default single-URL experience.
    /// </summary>
    public bool RouteAssignmentPending { get; set; }

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

    /// <summary>
    /// What this installation has registered with Home Assistant, keyed by unique id.
    /// Persisted so a sensor removed in a later version can be retired rather than
    /// left behind as an entity that never updates again.
    /// </summary>
    public Dictionary<string, RegisteredSensor> RegisteredSensors { get; set; } = new(StringComparer.Ordinal);

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

    /// <summary>The address configured for a route, or null when unset.</summary>
    public string? UrlFor(RouteKind route) =>
        route == RouteKind.Internal
            ? NullIfBlank(InternalUrl)
            : NullIfBlank(ExternalUrl);

    public bool HasRoute(RouteKind route) => UrlFor(route) is not null;

    /// <summary>Routes that actually have an address, in a stable order.</summary>
    public IReadOnlyList<RouteKind> ConfiguredRoutes()
    {
        var routes = new List<RouteKind>(2);
        if (HasRoute(RouteKind.Internal)) routes.Add(RouteKind.Internal);
        if (HasRoute(RouteKind.External)) routes.Add(RouteKind.External);
        return routes;
    }

    /// <summary>
    /// Records the address of a route and takes the install out of the migrated
    /// "not classified yet" state once either route is assigned.
    /// </summary>
    public void SetRoute(RouteKind route, string? url)
    {
        url = NullIfBlank(url);
        if (route == RouteKind.Internal) InternalUrl = url;
        else ExternalUrl = url;

        if (ConfiguredRoutes().Count > 0) RouteAssignmentPending = false;
    }

    /// <summary>Uses one address everywhere and clears route-specific settings.</summary>
    public void SetSingleUrl(string url)
    {
        BaseUrl = url;
        InternalUrl = null;
        ExternalUrl = null;
        UseSeparateUrls = false;
        ConnectionMode = ConnectionMode.Automatic;
        TrustedNetworks = new TrustedNetworkSettings();
        LastSuccessfulRoute = null;
        LastSuccessfulRouteAt = null;
        RouteAssignmentPending = false;
    }

    /// <summary>Marks a route as active, keeping <see cref="BaseUrl"/> in step.</summary>
    public void SetActiveRoute(RouteKind route, DateTimeOffset at)
    {
        var url = UrlFor(route) ?? throw new InvalidOperationException(
            $"The {route} address is not configured.");
        BaseUrl = url;
        LastSuccessfulRoute = route;
        LastSuccessfulRouteAt = at;
    }

    /// <summary>
    /// Brings earlier routing configurations forward. One address remains the
    /// default; two configured addresses imply that the user had opted into
    /// separate routing before this flag existed.
    /// </summary>
    /// <returns>True when the configuration changed and should be saved.</returns>
    public bool MigrateRoutes()
    {
        var routes = ConfiguredRoutes();
        if (routes.Count >= 2)
        {
            var changed = !UseSeparateUrls || RouteAssignmentPending;
            UseSeparateUrls = true;
            RouteAssignmentPending = false;
            return changed;
        }

        if (routes.Count == 1)
        {
            if (UseSeparateUrls)
            {
                if (!RouteAssignmentPending) return false;
                RouteAssignmentPending = false;
                return true;
            }

            var routeUrl = UrlFor(routes[0])!;
            SetSingleUrl(routeUrl);
            return true;
        }

        if (!RouteAssignmentPending && !UseSeparateUrls) return false;

        RouteAssignmentPending = false;
        UseSeparateUrls = false;
        return true;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
