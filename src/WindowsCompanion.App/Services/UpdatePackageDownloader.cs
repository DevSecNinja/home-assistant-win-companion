using WindowsCompanion.Core.App;
using WindowsCompanion.Core.Updates;

namespace WindowsCompanion_App.Services;

/// <summary>
/// Streams the selected setup package to
/// <c>%LOCALAPPDATA%\WindowsCompanion\Updates\&lt;version&gt;\</c>, reporting
/// progress and refusing to start when there is not enough free disk space.
/// </summary>
internal sealed class UpdatePackageDownloader : IUpdatePackageDownloader
{
    // The setup ZIP is extracted in-place before install, so require headroom
    // for both the downloaded ZIP and its extracted contents.
    private const double FreeSpaceHeadroomFactor = 3.0;
    private const int BufferSize = 81_920;
    private static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly string _updatesRoot;
    private readonly TimeSpan _stallTimeout;

    internal UpdatePackageDownloader(
        HttpClient http,
        string? updatesRootOverride = null,
        TimeSpan? stallTimeout = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _updatesRoot = updatesRootOverride
            ?? Path.Combine(AppDataPaths.Resolve(), "Updates");
        _stallTimeout = stallTimeout ?? DefaultStallTimeout;
        if (_stallTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(stallTimeout));
    }

    public async Task<string> DownloadAsync(
        SelectedUpdateAsset asset,
        SemanticVersion version,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(progress);

        var directory = Path.Combine(_updatesRoot, version.ToString());
        Directory.CreateDirectory(directory);

        var destination = Path.Combine(directory, asset.Package.Name);
        var partialDestination = destination + ".partial";

        using var request = new HttpRequestMessage(HttpMethod.Get, asset.Package.DownloadUrl);
        using var stalled = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            stalled.CancelAfter(_stallTimeout);
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stalled.Token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength;
            EnsureFreeSpace(directory, contentLength);

            await using (var source = await response.Content
                    .ReadAsStreamAsync(stalled.Token)
                    .ConfigureAwait(false))
            await using (var target = new FileStream(
                partialDestination,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                useAsync: true))
            {
                var buffer = new byte[BufferSize];
                long readTotal = 0;
                int read;
                while (true)
                {
                    stalled.CancelAfter(_stallTimeout);
                    read = await source
                        .ReadAsync(buffer, stalled.Token)
                        .ConfigureAwait(false);
                    if (read == 0) break;

                    await target
                        .WriteAsync(buffer.AsMemory(0, read), stalled.Token)
                        .ConfigureAwait(false);
                    readTotal += read;
                    if (contentLength is > 0)
                        progress.Report(Math.Clamp((double)readTotal / contentLength.Value, 0, 1));
                }
            }

            File.Copy(partialDestination, destination, overwrite: true);
            progress.Report(1);
            return destination;
        }
        catch (OperationCanceledException ex)
            when (!cancellationToken.IsCancellationRequested && stalled.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The update download made no progress for {_stallTimeout.TotalSeconds:0.#} seconds.",
                ex);
        }
        finally
        {
            TryDelete(partialDestination);
        }
    }

    private static void EnsureFreeSpace(string directory, long? contentLength)
    {
        if (contentLength is not > 0) return;

        var root = Path.GetPathRoot(Path.GetFullPath(directory));
        if (string.IsNullOrEmpty(root)) return;

        var drive = new DriveInfo(root);
        var required = (long)(contentLength.Value * FreeSpaceHeadroomFactor);
        if (drive.AvailableFreeSpace < required)
        {
            throw new IOException(
                $"Not enough free disk space on {root} to download and extract the update.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
