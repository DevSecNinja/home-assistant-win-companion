using WindowsCompanion.Core.Abstractions;

namespace WindowsCompanion.Core.Tests;

/// <summary>A fixed-token provider for tests.</summary>
internal sealed class StaticTokenProvider : IAccessTokenProvider
{
    private readonly string? _token;
    public StaticTokenProvider(string? token) => _token = token;
    public ValueTask<string?> GetAccessTokenAsync(CancellationToken ct = default) => new(_token);
}
