namespace HaCompanion_App.Services;

/// <summary>App-wide constants for the OAuth loopback flow and secret storage.</summary>
public static class AppConstants
{
    /// <summary>
    /// Fixed loopback port. Home Assistant validates that the refresh grant's
    /// client_id matches the one used at authorization, so this MUST be stable
    /// across restarts (verified against home-assistant/core auth token endpoint).
    /// </summary>
    public const int LoopbackPort = 8390;

    public static string RedirectUri => $"http://localhost:{LoopbackPort}/";

    /// <summary>client_id equals redirect_uri (same origin) so HA accepts it.</summary>
    public static string ClientId => RedirectUri;

    public const string RefreshTokenKey = "refresh_token";
}
