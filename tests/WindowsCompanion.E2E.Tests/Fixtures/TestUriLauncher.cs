using WindowsCompanion_App.Services;

namespace WindowsCompanion.E2E.Tests.Fixtures;

internal sealed class TestUriLauncher : IUriLauncher, IDisposable
{
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        UseCookies = false
    });
    private readonly object _gate = new();
    private readonly List<Uri> _launched = [];

    public IReadOnlyList<Uri> Launched
    {
        get
        {
            lock (_gate) return _launched.ToArray();
        }
    }

    public async Task LaunchAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        lock (_gate) _launched.Add(uri);

        using var response = await _http
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose() => _http.Dispose();
}
