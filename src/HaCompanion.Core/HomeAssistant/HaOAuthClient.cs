using System.Net.Http.Json;
using HaCompanion.Core.Models;

namespace HaCompanion.Core.HomeAssistant;

/// <summary>
/// Implements Home Assistant's OAuth2 (IndieAuth) flow for a native desktop app
/// using a loopback redirect. The <c>client_id</c> and <c>redirect_uri</c> are the
/// same loopback URL, which Home Assistant accepts because they share an origin
/// (verified against home-assistant/core indieauth.verify_redirect_uri).
/// </summary>
public sealed class HaOAuthClient
{
    private readonly HttpClient _http;
    private readonly Uri _baseUri;

    public HaOAuthClient(HttpClient http, string baseUrl)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            throw new ArgumentException("Base URL must be an absolute URI.", nameof(baseUrl));
        _baseUri = uri;
    }

    /// <summary>Builds the browser URL that starts the authorization flow.</summary>
    public static Uri BuildAuthorizeUrl(string baseUrl, string clientId, string redirectUri, string state)
    {
        var query = string.Join('&',
            "response_type=code",
            "client_id=" + Uri.EscapeDataString(clientId),
            "redirect_uri=" + Uri.EscapeDataString(redirectUri),
            "state=" + Uri.EscapeDataString(state));
        var b = new UriBuilder(new Uri(new Uri(baseUrl), "auth/authorize")) { Query = query };
        return b.Uri;
    }

    /// <summary>Exchanges an authorization code for access + refresh tokens.</summary>
    public Task<TokenResponse> ExchangeCodeAsync(string code, string clientId, CancellationToken ct = default)
        => PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = clientId
        }, ct);

    /// <summary>Exchanges a refresh token for a fresh access token.</summary>
    public Task<TokenResponse> RefreshAsync(string refreshToken, string clientId, CancellationToken ct = default)
        => PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId
        }, ct);

    /// <summary>Revokes a refresh token (used on sign-out).</summary>
    public async Task RevokeAsync(string refreshToken, CancellationToken ct = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = refreshToken,
            ["action"] = "revoke"
        });
        using var response = await _http.PostAsync(new Uri(_baseUri, "auth/token"), content, ct).ConfigureAwait(false);
        // Revoke always returns 200; ignore body.
    }

    private async Task<TokenResponse> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(new Uri(_baseUri, "auth/token"), content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TokenResponse>(ct).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Empty token response from Home Assistant.");
    }
}
