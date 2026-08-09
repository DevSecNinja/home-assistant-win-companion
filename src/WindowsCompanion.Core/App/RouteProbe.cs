using System.Text.Json;
using WindowsCompanion.Core.HomeAssistant;
using WindowsCompanion.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WindowsCompanion.Core.App;

/// <summary>Outcome of testing one address.</summary>
public enum RouteProbeStatus
{
    /// <summary>Reachable, is Home Assistant, and hosts this registration.</summary>
    Ok,

    /// <summary>Nothing answered in time.</summary>
    Unreachable,

    /// <summary>Something answered, but it is not a Home Assistant frontend.</summary>
    NotHomeAssistant,

    /// <summary>Home Assistant rejected the stored refresh token.</summary>
    CredentialsRejected,

    /// <summary>A Home Assistant that does not know this registration.</summary>
    DifferentInstance,

    /// <summary>Refused before any credential was sent (transport or redirect rule).</summary>
    Blocked
}

/// <param name="Route">Which address was tested.</param>
/// <param name="ResolvedUrl">The address after following redirects.</param>
/// <param name="InstanceDeviceId">Home Assistant's device id for this registration.</param>
/// <param name="InsecureTransport">True when the accepted address is plain HTTP.</param>
public sealed record RouteProbeResult(
    RouteKind Route,
    RouteProbeStatus Status,
    string? ResolvedUrl = null,
    string? InstanceDeviceId = null,
    string? Message = null,
    bool InsecureTransport = false)
{
    public bool Ok => Status == RouteProbeStatus.Ok;

    /// <summary>
    /// True when the address failed for a reason a different network could fix,
    /// as opposed to a configuration or identity problem that never will.
    /// </summary>
    public bool IsTransient => Status is RouteProbeStatus.Unreachable;
}

/// <summary>Tests whether an address can carry this session right now.</summary>
public interface IRouteProbe
{
    Task<RouteProbeResult> ProbeAsync(
        RouteKind route, string url, string? webhookId, CancellationToken ct = default);
}

/// <summary>
/// Validates an address in an order that never hands a credential to something
/// that has not first proved it is Home Assistant.
/// </summary>
/// <remarks>
/// The steps are: transport rules, then redirect resolution and its guards, then
/// an unauthenticated frontend identity check, then the refresh token, then the
/// authenticated API, and only last the webhook. That ordering is what makes a
/// captive portal or a hijacked DNS answer fail before it ever sees the token or
/// the webhook id.
/// </remarks>
public sealed class HttpRouteProbe : IRouteProbe
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(8);

    private readonly HttpClient _http;
    private readonly Func<string?> _refreshToken;
    private readonly string _clientId;
    private readonly TimeSpan _timeout;
    private readonly ILogger<HttpRouteProbe> _log;

    public HttpRouteProbe(
        HttpClient http,
        Func<string?> refreshTokenProvider,
        string clientId,
        TimeSpan? timeout = null,
        ILogger<HttpRouteProbe>? log = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _refreshToken = refreshTokenProvider ?? throw new ArgumentNullException(nameof(refreshTokenProvider));
        _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        _timeout = timeout ?? DefaultTimeout;
        _log = log ?? NullLogger<HttpRouteProbe>.Instance;
    }

    public async Task<RouteProbeResult> ProbeAsync(
        RouteKind route, string url, string? webhookId, CancellationToken ct = default)
    {
        var normalized = RouteUrlPolicy.Normalize(url, route);
        if (!normalized.Accepted || normalized.Url is null)
        {
            return new RouteProbeResult(route, RouteProbeStatus.Blocked,
                Message: normalized.Message ?? "The address is not usable.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeout);
        var token = timeout.Token;

        try
        {
            var identity = await CheckIdentityAsync(route, normalized.Url, token).ConfigureAwait(false);
            if (!identity.Ok) return identity;

            var resolved = identity.ResolvedUrl!;
            var refresh = _refreshToken();
            if (string.IsNullOrEmpty(refresh))
            {
                return identity with
                {
                    Status = RouteProbeStatus.CredentialsRejected,
                    Message = "No saved credentials to test this address with."
                };
            }

            TokenResponse tokens;
            try
            {
                tokens = await new HaOAuthClient(_http, resolved)
                    .RefreshAsync(refresh, _clientId, token)
                    .ConfigureAwait(false);
            }
            catch (HomeAssistantAuthException)
            {
                return identity with
                {
                    Status = RouteProbeStatus.CredentialsRejected,
                    Message = "This address did not accept the saved sign-in. It is most "
                              + "likely a different Home Assistant instance."
                };
            }

            var tokenManager = new OAuthTokenManager(
                new HaOAuthClient(_http, resolved), _clientId, () => refresh);
            tokenManager.Seed(tokens.AccessToken, tokens.ExpiresIn);
            var client = new HomeAssistantClient(_http, resolved, tokenManager);

            if (!await client.ValidateAsync(token).ConfigureAwait(false))
            {
                return identity with
                {
                    Status = RouteProbeStatus.NotHomeAssistant,
                    Message = "The Home Assistant API did not answer at this address."
                };
            }

            if (string.IsNullOrEmpty(webhookId))
            {
                // Nothing registered yet (first sign-in): the API check is all the
                // proof available, and there is no webhook to leak.
                return identity;
            }

            var info = await client.GetInstanceInfoAsync(webhookId, token).ConfigureAwait(false);
            if (info?.DeviceId is null)
            {
                return identity with
                {
                    Status = RouteProbeStatus.DifferentInstance,
                    Message = "This address answers as Home Assistant but does not know "
                              + "this PC's registration, so it is a different instance."
                };
            }

            return identity with { InstanceDeviceId = info.DeviceId };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new RouteProbeResult(route, RouteProbeStatus.Unreachable,
                Message: "The address did not answer in time.");
        }
        catch (HomeAssistantAuthException)
        {
            return new RouteProbeResult(route, RouteProbeStatus.CredentialsRejected,
                Message: "The saved sign-in was rejected at this address.");
        }
        catch (HttpRequestException ex)
        {
            _log.LogDebug("The {Route} address could not be reached: {Reason}.",
                route, ex.Message);
            return new RouteProbeResult(route, RouteProbeStatus.Unreachable,
                Message: "The address could not be reached from this network.");
        }
        catch (IOException ex)
        {
            // Something is listening but is not an HTTP server. Treat it as a
            // dead address rather than letting the raw transport error escape.
            _log.LogDebug("The {Route} address did not speak HTTP: {Reason}.",
                route, ex.Message);
            return new RouteProbeResult(route, RouteProbeStatus.NotHomeAssistant,
                Message: "The address did not speak the HTTP protocol expected by "
                         + "Home Assistant.");
        }
    }

    /// <summary>
    /// Follows redirects and confirms a Home Assistant frontend, using no
    /// credentials at all. The manifest is served unauthenticated by the frontend
    /// integration, so a captive portal or an unrelated host fails here - before
    /// the refresh token or the webhook id is sent anywhere.
    /// </summary>
    private async Task<RouteProbeResult> CheckIdentityAsync(
        RouteKind route, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(url), "manifest.json"));
        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);

        var finalUri = response.RequestMessage?.RequestUri;
        var resolved = finalUri is null
            ? url
            : finalUri.GetLeftPart(UriPartial.Authority) + "/";

        var redirect = RouteUrlPolicy.ValidateRedirect(url, resolved, route);
        if (!redirect.Accepted)
            return new RouteProbeResult(route, RouteProbeStatus.Blocked, Message: redirect.Message);

        if (!response.IsSuccessStatusCode)
        {
            return new RouteProbeResult(route, RouteProbeStatus.NotHomeAssistant, resolved,
                Message: "This address does not serve a Home Assistant frontend.");
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!LooksLikeHomeAssistant(body))
        {
            return new RouteProbeResult(route, RouteProbeStatus.NotHomeAssistant, resolved,
                Message: "Something answered at this address, but it is not Home Assistant. "
                         + "A sign-in portal on this network can look like this.");
        }

        return new RouteProbeResult(route, RouteProbeStatus.Ok, resolved,
            InsecureTransport: redirect.InsecureTransport);
    }

    internal static bool LooksLikeHomeAssistant(string manifestJson)
    {
        if (string.IsNullOrWhiteSpace(manifestJson)) return false;

        try
        {
            using var document = JsonDocument.Parse(manifestJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;

            var name = document.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null;
            var shortName = document.RootElement.TryGetProperty("short_name", out var s) ? s.GetString() : null;

            return string.Equals(name, "Home Assistant", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(shortName, "Assistant", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
