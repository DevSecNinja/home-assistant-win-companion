using WindowsCompanion.Core.Abstractions;
using WindowsCompanion.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WindowsCompanion.Core.App;

/// <summary>Why the active route is being reconsidered.</summary>
public enum RouteTrigger
{
    Startup,
    NetworkChanged,
    ConnectionFailed,
    UserRequested,
    Periodic
}

public enum RouteDecisionKind
{
    /// <summary>The active route is still the right one.</summary>
    Unchanged,

    /// <summary>A different route proved usable and is now active.</summary>
    Activated,

    /// <summary>Held back by the cooldown so a brief network blip cannot flap the route.</summary>
    Deferred,

    /// <summary>Nothing usable; the previous configuration is kept untouched.</summary>
    NoRouteAvailable
}

/// <param name="Route">The route in effect after the decision.</param>
/// <param name="Url">The address in effect, after redirects.</param>
public sealed record RouteDecision(
    RouteDecisionKind Kind,
    RouteKind? Route = null,
    string? Url = null,
    string? Reason = null,
    IReadOnlyList<RouteProbeResult>? Probes = null);

public sealed record RouteSupervisorOptions
{
    /// <summary>
    /// How long a freshly activated route is protected from being replaced by a
    /// network change. Connection failures bypass it, so a genuinely broken route
    /// still fails over immediately.
    /// </summary>
    public TimeSpan SwitchCooldown { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>How long to let the network settle before reacting to a change.</summary>
    public TimeSpan NetworkSettleDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Floor on unattended evaluations, so neither server is polled.</summary>
    public TimeSpan MinimumEvaluationInterval { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Owns which address is in use. It probes at most the configured addresses, in
/// the order <see cref="RouteSelector"/> allows, and only ever switches to a
/// route that has just proved it is the same Home Assistant instance.
/// </summary>
/// <remarks>
/// Switching keeps the refresh token, the webhook id, the device and every entity
/// id: nothing here registers anything. That is the whole point of validating
/// through <c>get_config</c> rather than through a second registration.
/// </remarks>
public sealed class RouteSupervisor
{
    private readonly ServerConfig _config;
    private readonly IRouteProbe _probe;
    private readonly IClock _clock;
    private readonly RouteSupervisorOptions _options;
    private readonly ILogger<RouteSupervisor> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private DateTimeOffset _activatedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _evaluatedAt = DateTimeOffset.MinValue;

    public RouteSupervisor(
        ServerConfig config,
        IRouteProbe probe,
        IClock? clock = null,
        RouteSupervisorOptions? options = null,
        ILogger<RouteSupervisor>? log = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _clock = clock ?? new SystemClock();
        _options = options ?? new RouteSupervisorOptions();
        _log = log ?? NullLogger<RouteSupervisor>.Instance;
    }

    public RouteKind? ActiveRoute { get; private set; }

    public string? ActiveUrl { get; private set; }

    public RouteStatus Status { get; private set; } = RouteStatus.Offline;

    /// <summary>True while the active address is plain HTTP.</summary>
    public bool ActiveTransportIsInsecure { get; private set; }

    /// <summary>HA Core version reported by the active route's last probe.</summary>
    public string? ActiveInstanceVersion { get; private set; }

    /// <summary>Raised after a route has proved usable and become active.</summary>
    public event Action<RouteDecision>? RouteActivated;

    /// <summary>Adopts a route that something else already brought up (resume).</summary>
    public void Adopt(RouteKind route, string url)
    {
        ActiveRoute = route;
        ActiveUrl = url;
        Status = route == RouteKind.Internal ? RouteStatus.Internal : RouteStatus.External;
        _activatedAt = _clock.UtcNow;
    }

    public async Task<RouteDecision> EvaluateAsync(
        NetworkContext network, RouteTrigger trigger, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(network);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await EvaluateCoreAsync(network, trigger, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<RouteDecision> EvaluateCoreAsync(
        NetworkContext network, RouteTrigger trigger, CancellationToken ct)
    {
        var now = _clock.UtcNow;

        if (trigger is RouteTrigger.Periodic
            && now - _evaluatedAt < _options.MinimumEvaluationInterval)
        {
            return new RouteDecision(RouteDecisionKind.Deferred, ActiveRoute, ActiveUrl,
                "Checked too recently.");
        }

        var plan = RouteSelector.Plan(_config, network);
        if (plan.Candidates.Count == 0)
        {
            Status = RouteStatus.Offline;
            return new RouteDecision(RouteDecisionKind.NoRouteAvailable, ActiveRoute, ActiveUrl,
                plan.Reason);
        }

        var preferred = plan.Candidates[0];

        if (ActiveRoute == preferred && trigger is RouteTrigger.NetworkChanged or RouteTrigger.Periodic)
        {
            _evaluatedAt = now;
            return new RouteDecision(RouteDecisionKind.Unchanged, ActiveRoute, ActiveUrl, plan.Reason);
        }

        // Hysteresis: a route that has only just been proven is not abandoned
        // because the adapter blinked. A real failure arrives as ConnectionFailed
        // and is never deferred.
        if (ActiveRoute is { } active
            && trigger is RouteTrigger.NetworkChanged or RouteTrigger.Periodic
            && plan.Candidates.Contains(active)
            && now - _activatedAt < _options.SwitchCooldown)
        {
            _evaluatedAt = now;
            return new RouteDecision(RouteDecisionKind.Deferred, ActiveRoute, ActiveUrl,
                "Waiting for the connection to settle before switching.");
        }

        if (ActiveRoute is not null && trigger == RouteTrigger.ConnectionFailed)
            Status = RouteStatus.FailingOver;

        _evaluatedAt = now;
        var probes = new List<RouteProbeResult>(plan.Candidates.Count);

        foreach (var candidate in plan.Candidates)
        {
            ct.ThrowIfCancellationRequested();
            var url = _config.UrlFor(candidate);
            if (url is null) continue;

            var result = await _probe.ProbeAsync(candidate, url, _config.WebhookId, ct).ConfigureAwait(false);
            probes.Add(result);

            if (!result.Ok)
            {
                _log.LogInformation("The {Route} address is not usable right now ({Status}).",
                    candidate, result.Status);
                continue;
            }

            if (!MatchesKnownInstance(result))
            {
                _log.LogWarning(
                    "The {Route} address answers as a different Home Assistant instance; ignoring it.",
                    candidate);
                probes[^1] = result with
                {
                    Status = RouteProbeStatus.DifferentInstance,
                    Message = "This address is a different Home Assistant instance than the one "
                              + "this PC is registered with."
                };
                continue;
            }

            return Activate(candidate, result, plan.Reason, probes, now);
        }

        Status = RouteStatus.Offline;
        _log.LogWarning("No configured address is usable on this network.");
        return new RouteDecision(RouteDecisionKind.NoRouteAvailable, ActiveRoute, ActiveUrl,
            "No configured address answered.", probes);
    }

    private bool MatchesKnownInstance(RouteProbeResult result)
    {
        if (result.InstanceDeviceId is null) return true; // nothing registered yet
        return string.IsNullOrEmpty(_config.InstanceDeviceId)
               || string.Equals(_config.InstanceDeviceId, result.InstanceDeviceId, StringComparison.Ordinal);
    }

    private RouteDecision Activate(
        RouteKind route,
        RouteProbeResult result,
        string reason,
        List<RouteProbeResult> probes,
        DateTimeOffset now)
    {
        var url = result.ResolvedUrl ?? _config.UrlFor(route)!;
        var unchanged = ActiveRoute == route
                        && string.Equals(ActiveUrl, url, StringComparison.OrdinalIgnoreCase);

        _config.SetRoute(route, url);
        _config.SetActiveRoute(route, now);
        if (result.InstanceDeviceId is not null && string.IsNullOrEmpty(_config.InstanceDeviceId))
            _config.InstanceDeviceId = result.InstanceDeviceId;

        ActiveRoute = route;
        ActiveUrl = url;
        ActiveTransportIsInsecure = result.InsecureTransport;
        ActiveInstanceVersion = result.InstanceVersion;
        Status = route == RouteKind.Internal ? RouteStatus.Internal : RouteStatus.External;
        _activatedAt = now;

        var decision = new RouteDecision(
            unchanged ? RouteDecisionKind.Unchanged : RouteDecisionKind.Activated,
            route, url, reason, probes);

        if (!unchanged)
        {
            _log.LogInformation("Switched to the {Route} Home Assistant address ({Reason})", route, reason);
            RouteActivated?.Invoke(decision);
        }

        return decision;
    }
}
