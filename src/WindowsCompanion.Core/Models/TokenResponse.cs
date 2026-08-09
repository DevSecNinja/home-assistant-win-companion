using System.Text.Json.Serialization;

namespace WindowsCompanion.Core.Models;

/// <summary>Response from the Home Assistant /auth/token endpoint.</summary>
public sealed class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    /// <summary>Only returned by the authorization_code grant, not by refresh.</summary>
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "Bearer";
}
