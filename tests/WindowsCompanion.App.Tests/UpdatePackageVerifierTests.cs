using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using WindowsCompanion.Core.Updates;
using WindowsCompanion_App.Services;

namespace WindowsCompanion.App.Tests;

public class UpdatePackageVerifierTests
{
    private const string FileName = "WindowsCompanion-1.2.3-win-x64-setup.zip";
    private static readonly string Hash1 = string.Concat(Enumerable.Repeat("ab", 32));
    private static readonly string Hash2 = string.Concat(Enumerable.Repeat("cd", 32));

    public static IEnumerable<object[]> SidecarFormats()
    {
        yield return [$"{Hash1}  {FileName}", FileName, Hash1];
        yield return [$"*{FileName}\n{Hash2}  {FileName}", FileName, Hash2];
        yield return [Hash1, FileName, Hash1];
    }

    [Theory]
    [MemberData(nameof(SidecarFormats))]
    public void Checksum_sidecars_are_parsed_for_the_expected_file(
        string sidecar,
        string expectedFileName,
        string expectedHash)
    {
        var hash = UpdatePackageVerifier.ParseChecksumSidecar(sidecar, expectedFileName);

        Assert.Equal(expectedHash, hash);
    }

    [Fact]
    public void A_sidecar_naming_a_different_file_is_rejected()
    {
        var hash = UpdatePackageVerifier.ParseChecksumSidecar(
            $"{Hash1}  some-other-file.zip",
            FileName);

        Assert.Null(hash);
    }

    [Fact]
    public void Garbage_sidecar_content_does_not_parse()
    {
        Assert.Null(UpdatePackageVerifier.ParseChecksumSidecar(
            "not a checksum",
            "WindowsCompanion-1.2.3-win-x64-setup.zip"));
    }

    [Fact]
    public async Task A_checksum_mismatch_fails_closed_before_any_attestation_lookup()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), $"wc-verify-{Guid.NewGuid():N}.zip");
        await File.WriteAllTextAsync(packagePath, "package-bytes");
        try
        {
            var attestationRequested = false;
            var handler = new DelegateHandler((request, _) =>
            {
                if (request.RequestUri!.AbsoluteUri.Contains("attestations"))
                    attestationRequested = true;

                return Task.FromResult(TextResponse(
                    "0000000000000000000000000000000000000000000000000000000000000000  WindowsCompanion-1.2.3-win-x64-setup.zip"));
            });
            var verifier = new UpdatePackageVerifier(
                new HttpClient(handler),
                "1.2.3",
                NullLogger<UpdatePackageVerifier>.Instance);
            var asset = new SelectedUpdateAsset(
                new ReleaseAsset(
                    "WindowsCompanion-1.2.3-win-x64-setup.zip",
                    "https://example.invalid/package.zip"),
                new ReleaseAsset(
                    "WindowsCompanion-1.2.3-win-x64-setup.zip.sha256",
                    "https://example.invalid/package.zip.sha256"));

            await Assert.ThrowsAsync<UpdatePackageVerificationException>(
                () => verifier.VerifyAsync(packagePath, asset, CancellationToken.None));

            Assert.False(attestationRequested);
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Fact]
    public async Task A_release_asset_digest_disagreeing_with_the_sidecar_fails_closed()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), $"wc-verify-{Guid.NewGuid():N}.zip");
        var bytes = "package-bytes"u8.ToArray();
        await File.WriteAllBytesAsync(packagePath, bytes);
        try
        {
            var actualHash = Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(bytes));
            var handler = new DelegateHandler((_, _) => Task.FromResult(TextResponse(
                $"{actualHash}  WindowsCompanion-1.2.3-win-x64-setup.zip")));
            var verifier = new UpdatePackageVerifier(
                new HttpClient(handler),
                "1.2.3",
                NullLogger<UpdatePackageVerifier>.Instance);
            var asset = new SelectedUpdateAsset(
                new ReleaseAsset(
                    "WindowsCompanion-1.2.3-win-x64-setup.zip",
                    "https://example.invalid/package.zip",
                    DigestSha256: new string('f', 64)),
                new ReleaseAsset(
                    "WindowsCompanion-1.2.3-win-x64-setup.zip.sha256",
                    "https://example.invalid/package.zip.sha256"));

            await Assert.ThrowsAsync<UpdatePackageVerificationException>(
                () => verifier.VerifyAsync(packagePath, asset, CancellationToken.None));
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Fact]
    public async Task No_published_attestations_fail_closed_after_a_passing_checksum()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), $"wc-verify-{Guid.NewGuid():N}.zip");
        var bytes = "package-bytes"u8.ToArray();
        await File.WriteAllBytesAsync(packagePath, bytes);
        try
        {
            var actualHash = Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(bytes));
            var handler = new DelegateHandler((request, _) =>
                Task.FromResult(request.RequestUri!.AbsoluteUri.Contains("attestations")
                    ? TextResponse("""{"attestations":[]}""")
                    : TextResponse($"{actualHash}  WindowsCompanion-1.2.3-win-x64-setup.zip")));
            var verifier = new UpdatePackageVerifier(
                new HttpClient(handler),
                "1.2.3",
                NullLogger<UpdatePackageVerifier>.Instance);
            var asset = new SelectedUpdateAsset(
                new ReleaseAsset(
                    "WindowsCompanion-1.2.3-win-x64-setup.zip",
                    "https://example.invalid/package.zip"),
                new ReleaseAsset(
                    "WindowsCompanion-1.2.3-win-x64-setup.zip.sha256",
                    "https://example.invalid/package.zip.sha256"));

            var ex = await Assert.ThrowsAsync<UpdatePackageVerificationException>(
                () => verifier.VerifyAsync(packagePath, asset, CancellationToken.None));
            Assert.Contains("attestation", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    private static HttpResponseMessage TextResponse(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }
}
