namespace WindowsCompanion.Core.Abstractions;

/// <summary>
/// Supplies a currently-valid Home Assistant access token, refreshing it
/// transparently when it is missing or about to expire.
/// </summary>
public interface IAccessTokenProvider
{
    ValueTask<string?> GetAccessTokenAsync(CancellationToken ct = default);
}
