using System.Net;
using System.Net.Http.Headers;
using WindowsCompanion.Core.Updates;

namespace WindowsCompanion_App.Services;

/// <summary>Reads public release metadata from GitHub's official REST endpoint.</summary>
internal sealed class GitHubReleaseClient : IReleaseSource
{
    internal static readonly Uri ReleasesEndpoint = new(
        "https://api.github.com/repos/DevSecNinja/home-assistant-win-companion/releases/latest");

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private const int MaximumResponseBytes = 1_048_576;
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
        if (content.Headers.ContentLength > MaximumResponseBytes)
            throw new InvalidDataException("The GitHub releases response was too large.");

        await using var stream = await content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream
                .ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0) break;
            if (bytes.Length + read > MaximumResponseBytes)
                throw new InvalidDataException("The GitHub releases response was too large.");
            await bytes.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }

        bytes.Position = 0;
        using var reader = new StreamReader(
            bytes,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: false);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Creates the client used to fetch release assets (setup ZIPs and checksum
    /// sidecars) from their published <c>browser_download_url</c>. Unlike
    /// <see cref="CreateHttpClient"/>, which pins the GitHub Releases REST API
    /// to same-host responses, GitHub always answers asset download requests
    /// with a redirect to a signed, time-limited URL on its object storage
    /// host, so this client must follow redirects to succeed.
    /// </summary>
    internal static HttpClient CreateAssetDownloadHttpClient() =>
        new(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
}
