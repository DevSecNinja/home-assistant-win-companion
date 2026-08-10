using System.Net;
using System.Net.Http.Headers;
using System.Text;
using WindowsCompanion.Core.Updates;

namespace WindowsCompanion_App.Services;

/// <summary>Reads public release metadata from GitHub's official REST endpoint.</summary>
internal sealed class GitHubReleaseClient : IReleaseSource
{
    internal static readonly Uri ReleasesEndpoint = new(
        "https://api.github.com/repos/DevSecNinja/home-assistant-win-companion/releases/latest");

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private const int MaximumResponseCharacters = 1_048_576;
    private readonly HttpClient _http;
    private readonly string _productVersion;
    private readonly TimeSpan _timeout;

    internal GitHubReleaseClient(
        HttpClient http,
        string productVersion,
        TimeSpan? timeout = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _productVersion = productVersion;
        _timeout = timeout ?? DefaultTimeout;
    }

    public async Task<IReadOnlyList<ReleaseCandidate>> GetReleasesAsync(
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesEndpoint);
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.Add(
                new ProductInfoHeaderValue("WindowsCompanion", _productVersion));
            request.Headers.UserAgent.Add(
                new ProductInfoHeaderValue("(+https://github.com/DevSecNinja/home-assistant-win-companion)"));
            request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");

            using var response = await _http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await ReadResponseAsync(response.Content, timeout.Token)
                .ConfigureAwait(false);
            return ReleaseCatalogParser.Parse(json);
        }

        catch (OperationCanceledException ex)
            when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The GitHub release request exceeded the {_timeout.TotalSeconds:0.#}-second timeout.",
                ex);
        }
    }

    private static async Task<string> ReadResponseAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaximumResponseCharacters)
            throw new InvalidDataException("The GitHub releases response was too large.");

        await using var stream = await content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: false);

        var result = new StringBuilder();
        var buffer = new char[8192];
        while (true)
        {
            var read = await reader
                .ReadAsync(buffer.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0) return result.ToString();
            if (result.Length + read > MaximumResponseCharacters)
                throw new InvalidDataException("The GitHub releases response was too large.");
            result.Append(buffer, 0, read);
        }
    }

    internal static HttpClient CreateHttpClient() =>
        new(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
}
