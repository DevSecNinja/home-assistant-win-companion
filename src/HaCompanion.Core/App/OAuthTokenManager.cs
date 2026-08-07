using HaCompanion.Core.Abstractions;
using HaCompanion.Core.HomeAssistant;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HaCompanion.Core.App;

/// <summary>
/// Caches a Home Assistant access token and refreshes it from the stored refresh
/// token when it is missing or within a safety window of expiry. Thread-safe.
/// </summary>
public sealed class OAuthTokenManager : IAccessTokenProvider
{
    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(60);

    private readonly HaOAuthClient _oauth;
    private readonly string _clientId;
    private readonly Func<string?> _refreshTokenProvider;
    private readonly IClock _clock;
    private readonly ILogger<OAuthTokenManager> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public OAuthTokenManager(
        HaOAuthClient oauth,
        string clientId,
        Func<string?> refreshTokenProvider,
        IClock? clock = null,
        ILogger<OAuthTokenManager>? log = null)
    {
        _oauth = oauth ?? throw new ArgumentNullException(nameof(oauth));
        _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        _refreshTokenProvider = refreshTokenProvider ?? throw new ArgumentNullException(nameof(refreshTokenProvider));
        _clock = clock ?? new SystemClock();
        _log = log ?? NullLogger<OAuthTokenManager>.Instance;
    }

    /// <summary>Seeds the cache with the tokens obtained from the initial login.</summary>
    public void Seed(string accessToken, int expiresInSeconds)
    {
        _accessToken = accessToken;
        _expiresAt = _clock.UtcNow.AddSeconds(expiresInSeconds);
    }

    public async ValueTask<string?> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (_accessToken is not null && _clock.UtcNow < _expiresAt - Skew)
            return _accessToken;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_accessToken is not null && _clock.UtcNow < _expiresAt - Skew)
                return _accessToken;

            var refresh = _refreshTokenProvider();
            if (string.IsNullOrEmpty(refresh))
            {
                _log.LogWarning("No refresh token available; cannot obtain an access token.");
                return null;
            }

            var token = await _oauth.RefreshAsync(refresh, _clientId, ct).ConfigureAwait(false);
            _accessToken = token.AccessToken;
            _expiresAt = _clock.UtcNow.AddSeconds(token.ExpiresIn);
            _log.LogDebug("Refreshed Home Assistant access token.");
            return _accessToken;
        }
        finally
        {
            _gate.Release();
        }
    }
}
