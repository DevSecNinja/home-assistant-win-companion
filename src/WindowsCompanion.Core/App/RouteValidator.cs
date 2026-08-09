using WindowsCompanion.Core.Models;

namespace WindowsCompanion.Core.App;

/// <summary>The connection settings the user is proposing, before validation.</summary>
public sealed record ConnectionSettingsDraft
{
    public string? PrimaryUrl { get; init; }
    public bool UseSeparateUrls { get; init; }
    public string? InternalUrl { get; init; }
    public string? ExternalUrl { get; init; }
    public ConnectionMode Mode { get; init; } = ConnectionMode.Automatic;
    public TrustedNetworkSettings TrustedNetworks { get; init; } = new();

    /// <summary>
    /// The user has confirmed that an address is deliberately unreachable from the
    /// network they are on right now, so it may be saved unvalidated and checked
    /// again when it can actually be reached.
    /// </summary>
    public bool AcknowledgeUnreachable { get; init; }
}

/// <param name="Url">Result of normalizing and applying the transport rules.</param>
/// <param name="Probe">Result of testing the address, when it was tested at all.</param>
public sealed record RouteValidationEntry(
    RouteKind Route,
    RouteUrlResult Url,
    RouteProbeResult? Probe)
{
    public bool Validated => Probe is { Ok: true };

    public bool NeedsAcknowledgement => Probe is { Status: RouteProbeStatus.Unreachable };

    public string Describe() => Url.Problem switch
    {
        RouteUrlProblem.None when Probe is null => "Not configured.",
        RouteUrlProblem.None => Probe!.Status switch
        {
            RouteProbeStatus.Ok when Url.InsecureTransport =>
                "Reachable, same Home Assistant instance (plain HTTP).",
            RouteProbeStatus.Ok => "Reachable, same Home Assistant instance.",
            RouteProbeStatus.Unreachable =>
                "Not reachable from this network. Save it only if you expect that here.",
            _ => Probe.Message ?? "Not usable."
        },
        _ => Url.Message ?? "Not usable."
    };
}

/// <param name="CanSave">Whether the draft may replace the working configuration.</param>
public sealed record RouteValidationReport(
    IReadOnlyList<RouteValidationEntry> Entries,
    bool CanSave,
    string Summary,
    string? InstanceDeviceId = null,
    bool RequiresAcknowledgement = false,
    bool RequiresSignIn = false)
{
    public RouteValidationEntry? For(RouteKind route) =>
        Entries.FirstOrDefault(e => e.Route == route);
}

/// <summary>
/// Proves that two addresses are the same Home Assistant instance before either
/// is saved, so a route switch can never invalidate the refresh token, the
/// webhook, the device or its history.
/// </summary>
/// <remarks>
/// Identity is taken from Home Assistant's own device-registry id for this
/// registration (<c>get_config</c>), not from the instance name or version, which
/// two unrelated servers can trivially share. Nothing here registers a device, so
/// testing an address never produces a second Mobile App entry.
/// </remarks>
public static class RouteValidator
{
    public static async Task<RouteValidationReport> ValidateAsync(
        ServerConfig current,
        ConnectionSettingsDraft draft,
        IRouteProbe probe,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(probe);

        if (!draft.UseSeparateUrls)
            return await ValidateSingleAsync(current, draft, probe, ct).ConfigureAwait(false);

        var internalUrl = RouteUrlPolicy.Normalize(draft.InternalUrl, RouteKind.Internal);
        var externalUrl = RouteUrlPolicy.Normalize(draft.ExternalUrl, RouteKind.External);

        var entries = new List<RouteValidationEntry>(2);

        if (!internalUrl.Accepted || !externalUrl.Accepted)
        {
            entries.Add(new RouteValidationEntry(RouteKind.Internal, internalUrl, null));
            entries.Add(new RouteValidationEntry(RouteKind.External, externalUrl, null));
            var message = (internalUrl.Accepted ? externalUrl.Message : internalUrl.Message)
                          ?? "One of the addresses is not usable.";
            return new RouteValidationReport(entries, false, message);
        }

        if (internalUrl.Url is null && externalUrl.Url is null)
        {
            return new RouteValidationReport([], false,
                "Enter at least one Home Assistant address.");
        }

        if (ModeRequirementUnmet(draft, internalUrl.Url, externalUrl.Url) is { } modeProblem)
            return new RouteValidationReport([], false, modeProblem);

        foreach (var (route, url) in new[]
                 {
                     (RouteKind.Internal, internalUrl),
                     (RouteKind.External, externalUrl)
                 })
        {
            if (url.Url is null)
            {
                entries.Add(new RouteValidationEntry(route, url, null));
                continue;
            }

            var result = await probe.ProbeAsync(route, url.Url, current.WebhookId, ct).ConfigureAwait(false);
            entries.Add(new RouteValidationEntry(route, url with { Url = result.ResolvedUrl ?? url.Url }, result));
        }

        return Judge(current, draft, entries, singleUrl: false);
    }

    private static async Task<RouteValidationReport> ValidateSingleAsync(
        ServerConfig current,
        ConnectionSettingsDraft draft,
        IRouteProbe probe,
        CancellationToken ct)
    {
        var url = RouteUrlPolicy.Normalize(draft.PrimaryUrl, RouteKind.Internal);
        if (!url.Accepted)
        {
            return new RouteValidationReport(
                [new RouteValidationEntry(RouteKind.Internal, url, null)],
                false,
                url.Message ?? "The address is not usable.");
        }

        if (url.Url is null)
            return new RouteValidationReport([], false, "Enter a Home Assistant address.");

        var result = await probe
            .ProbeAsync(RouteKind.Internal, url.Url, current.WebhookId, ct)
            .ConfigureAwait(false);
        var entry = new RouteValidationEntry(
            RouteKind.Internal,
            url with { Url = result.ResolvedUrl ?? url.Url },
            result);
        return Judge(current, draft, [entry], singleUrl: true);
    }

    private static string? ModeRequirementUnmet(
        ConnectionSettingsDraft draft, string? internalUrl, string? externalUrl) =>
        draft.Mode switch
        {
            ConnectionMode.InternalOnly when internalUrl is null =>
                "Internal only needs an internal address.",
            ConnectionMode.ExternalOnly when externalUrl is null =>
                "External only needs an external address.",
            _ => null
        };

    private static RouteValidationReport Judge(
        ServerConfig current,
        ConnectionSettingsDraft draft,
        List<RouteValidationEntry> entries,
        bool singleUrl)
    {
        var tested = entries.Where(e => e.Probe is not null).ToList();

        if (tested.Any(e => e.Probe!.Status == RouteProbeStatus.CredentialsRejected))
        {
            return new RouteValidationReport(entries, false,
                singleUrl
                    ? "The address did not accept this PC's saved sign-in. Nothing was changed."
                    : "One address did not accept this PC's saved sign-in, so it is not the same "
                      + "Home Assistant instance. Nothing was changed.",
                RequiresSignIn: true);
        }

        if (tested.Any(e => e.Probe!.Status == RouteProbeStatus.DifferentInstance))
        {
            return new RouteValidationReport(entries, false,
                singleUrl
                    ? "The address points at a different Home Assistant instance."
                    : "One address points at a different Home Assistant instance. Both addresses "
                      + "must reach the same instance for the device, entities and history to survive.",
                RequiresSignIn: true);
        }

        var blocked = tested.FirstOrDefault(e =>
            e.Probe!.Status is RouteProbeStatus.Blocked or RouteProbeStatus.NotHomeAssistant);
        if (blocked is not null)
        {
            return new RouteValidationReport(entries, false,
                blocked.Probe!.Message ?? "One address is not usable. Nothing was changed.");
        }

        var identities = tested
            .Where(e => e.Probe!.Ok && e.Probe.InstanceDeviceId is not null)
            .Select(e => e.Probe!.InstanceDeviceId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (identities.Count > 1)
        {
            return new RouteValidationReport(entries, false,
                "The two addresses answered as different Home Assistant instances.",
                RequiresSignIn: true);
        }

        if (identities.Count == 1
            && !string.IsNullOrEmpty(current.InstanceDeviceId)
            && !string.Equals(current.InstanceDeviceId, identities[0], StringComparison.Ordinal))
        {
            return new RouteValidationReport(entries, false,
                (singleUrl ? "This address reaches" : "These addresses reach")
                + " a different Home Assistant instance than this PC is registered with. "
                + "Remove the server and sign in again to move instance.",
                RequiresSignIn: true);
        }

        var unreachable = tested.Where(e => e.NeedsAcknowledgement).ToList();
        if (unreachable.Count > 0 && !draft.AcknowledgeUnreachable)
        {
            return new RouteValidationReport(entries, false,
                singleUrl
                    ? "The address could not be reached from this network. Confirm that this is "
                      + "expected to save it anyway."
                    : $"The {Describe(unreachable)} address could not be reached from this network. "
                      + "Confirm that this is expected to save it anyway; it is checked again when "
                      + "it can be reached.",
                RequiresAcknowledgement: true);
        }

        if (!tested.Any(e => e.Probe!.Ok))
        {
            return new RouteValidationReport(entries, false,
                singleUrl
                    ? "The address could not be validated, so the previous configuration was kept."
                    : "Neither address could be validated, so the previous configuration was kept.",
                RequiresAcknowledgement: unreachable.Count > 0);
        }

        return new RouteValidationReport(entries, true,
            unreachable.Count == 0
                ? singleUrl
                    ? "The address reaches this Home Assistant instance."
                    : "Both addresses reach the same Home Assistant instance."
                : singleUrl
                    ? "Saved; the address will be validated when it can be reached."
                    : $"Saved; the {Describe(unreachable)} address will be validated when it can be reached.",
            identities.FirstOrDefault());
    }

    private static string Describe(IEnumerable<RouteValidationEntry> entries) =>
        string.Join(" and ", entries.Select(e => e.Route == RouteKind.Internal ? "internal" : "external"));

    /// <summary>
    /// Writes a validated draft into the configuration. Only ever called after
    /// <see cref="ValidateAsync"/> approved it, so the previous working
    /// configuration survives every failure path untouched.
    /// </summary>
    public static void Apply(ServerConfig config, ConnectionSettingsDraft draft, RouteValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(report);
        if (!report.CanSave) throw new InvalidOperationException("The draft was not validated.");

        if (!draft.UseSeparateUrls)
        {
            var url = report.For(RouteKind.Internal)?.Url.Url
                      ?? throw new InvalidOperationException("The single address was not validated.");
            config.SetSingleUrl(url);
            if (!string.IsNullOrEmpty(report.InstanceDeviceId))
                config.InstanceDeviceId = report.InstanceDeviceId;
            return;
        }

        config.SetRoute(RouteKind.Internal, report.For(RouteKind.Internal)?.Url.Url);
        config.SetRoute(RouteKind.External, report.For(RouteKind.External)?.Url.Url);
        config.UseSeparateUrls = true;
        config.ConnectionMode = draft.Mode;
        config.TrustedNetworks = draft.TrustedNetworks;
        config.RouteAssignmentPending = false;

        if (!string.IsNullOrEmpty(report.InstanceDeviceId))
            config.InstanceDeviceId = report.InstanceDeviceId;
    }
}
