using System.Net;
using System.Text;
using WindowsCompanion.Core.Updates;
using WindowsCompanion_App.Services;

namespace WindowsCompanion.App.Tests;

public class UpdatePackageDownloaderTests
{
    [Fact]
    public async Task Downloaded_bytes_are_written_and_progress_reaches_one()
    {
        var payload = Encoding.UTF8.GetBytes(new string('a', 10_000));
        var handler = new DelegateHandler((_, _) => Task.FromResult(BinaryResponse(payload)));
        var root = Path.Combine(Path.GetTempPath(), $"wc-dl-{Guid.NewGuid():N}");
        var downloader = new UpdatePackageDownloader(new HttpClient(handler), root);
        var asset = MakeAsset("WindowsCompanion-1.2.3-win-x64-setup.zip");
        var version = ParseVersion("1.2.3");
        var reports = new List<double>();
        var progress = new InlineProgress<double>(reports.Add);

        try
        {
            var path = await downloader.DownloadAsync(asset, version, progress, CancellationToken.None);

            Assert.True(File.Exists(path));
            Assert.Equal(payload, await File.ReadAllBytesAsync(path));
            Assert.Contains(1.0, reports);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task An_implausibly_large_download_is_rejected_before_streaming()
    {
        var handler = new DelegateHandler((_, _) =>
        {
            var response = BinaryResponse([1, 2, 3]);
            response.Content.Headers.ContentLength = long.MaxValue / 4;
            return Task.FromResult(response);
        });
        var root = Path.Combine(Path.GetTempPath(), $"wc-dl-{Guid.NewGuid():N}");
        var downloader = new UpdatePackageDownloader(new HttpClient(handler), root);
        var asset = MakeAsset("WindowsCompanion-1.2.3-win-x64-setup.zip");

        try
        {
            await Assert.ThrowsAsync<IOException>(
                () => downloader.DownloadAsync(
                    asset,
                    ParseVersion("1.2.3"),
                    new Progress<double>(),
                    CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_download_that_stalls_before_response_headers_times_out()
    {
        var handler = new DelegateHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The cancellation token should stop the request.");
        });
        var root = Path.Combine(Path.GetTempPath(), $"wc-dl-{Guid.NewGuid():N}");
        var downloader = new UpdatePackageDownloader(
            new HttpClient(handler),
            root,
            TimeSpan.FromMilliseconds(25));
        var asset = MakeAsset("WindowsCompanion-1.2.3-win-x64-setup.zip");

        try
        {
            await Assert.ThrowsAsync<TimeoutException>(
                () => downloader.DownloadAsync(
                    asset,
                    ParseVersion("1.2.3"),
                    new Progress<double>(),
                    CancellationToken.None));
            Assert.Empty(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static SelectedUpdateAsset MakeAsset(string packageName) => new(
        new ReleaseAsset(packageName, $"https://example.invalid/{packageName}"),
        new ReleaseAsset(packageName + ".sha256", $"https://example.invalid/{packageName}.sha256"));

    private static SemanticVersion ParseVersion(string value)
    {
        Assert.True(SemanticVersion.TryParse(value, out var version));
        return version!;
    }

    private static HttpResponseMessage BinaryResponse(byte[] bytes) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
