using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sigstore;
using WindowsCompanion.Core.Updates;

namespace WindowsCompanion_App.Services;

/// <summary>
/// Confirms a downloaded setup package matches its published SHA256 checksum
/// sidecar and carries a GitHub build-provenance attestation tying it back to
/// the exact tagged release build of this repository's release workflow. Both
/// checks must pass (fail-closed); either failing throws
/// <see cref="UpdatePackageVerificationException"/> so the installer discards
/// the package and falls back to the manual "open release page" path.
/// </summary>
internal sealed class UpdatePackageVerifier : IUpdatePackageVerifier
{
    internal const string Owner = "DevSecNinja";
    internal const string Repository = "home-assistant-win-companion";
    internal const string WorkflowFileName = ".github/workflows/release.yml";
    internal const string DefaultBranch = "main";

    private const long MaxChecksumSidecarBytes = 4_096;
    private const long MaxAttestationResponseBytes = 4_194_304;

    private readonly HttpClient _http;
    private readonly HttpClient _assetHttp;
    private readonly string _productVersion;
    private readonly SigstoreVerifier _sigstore;
    private readonly ILogger<UpdatePackageVerifier> _log;

    internal UpdatePackageVerifier(
        HttpClient http,
        string productVersion,
        ILogger<UpdatePackageVerifier> log,
        HttpClient? assetHttp = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        // The checksum sidecar is fetched from its published
        // browser_download_url, which GitHub answers with a redirect to its
        // object storage host; a caller may supply a redirect-following
        // client for that request while keeping the pinned, non-redirecting
        // client for the same-host GitHub REST API attestation lookup below.
        _assetHttp = assetHttp ?? http;
        _productVersion = productVersion;
        _log = log ?? throw new ArgumentNullException(nameof(log));
        // The default constructor fetches the Sigstore public-good trust root
        // on first use; this requires network access, which update checks
        // already assume.
        _sigstore = new SigstoreVerifier();
    }

    public async Task VerifyAsync(
        string packagePath,
        SelectedUpdateAsset asset,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);

        var digest = await VerifyChecksumAsync(packagePath, asset, cancellationToken)
            .ConfigureAwait(false);
        await VerifyAttestationAsync(packagePath, asset, digest, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string> VerifyChecksumAsync(
        string packagePath,
        SelectedUpdateAsset asset,
        CancellationToken cancellationToken)
    {
        var sidecarText = await GetStringAsync(
                _assetHttp,
                asset.Checksum.DownloadUrl,
                MaxChecksumSidecarBytes,
                cancellationToken)
            .ConfigureAwait(false);

        var expected = ParseChecksumSidecar(sidecarText, asset.Package.Name);
        if (expected is null)
        {
            throw new UpdatePackageVerificationException(
                $"The checksum sidecar for {asset.Package.Name} could not be parsed.");
        }

        string actual;
        await using (var stream = File.OpenRead(packagePath))
        {
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            actual = Convert.ToHexStringLower(hash);
        }

        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdatePackageVerificationException(
                $"The downloaded package's SHA256 checksum did not match the published sidecar for {asset.Package.Name}.");
        }

        // The GitHub release API's own asset digest (when present) is an
        // independent second source for the same value; a mismatch here would
        // mean the release metadata and the sidecar disagree, which is treated
        // as a verification failure rather than silently trusted.
        if (asset.Package.DigestSha256 is { Length: > 0 } releaseDigest
            && !string.Equals(actual, releaseDigest, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdatePackageVerificationException(
                $"The downloaded package's checksum did not match the release asset digest for {asset.Package.Name}.");
        }

        return actual;
    }

    private async Task VerifyAttestationAsync(
        string packagePath,
        SelectedUpdateAsset asset,
        string digest,
        CancellationToken cancellationToken)
    {
        var attestationsJson = await GetStringAsync(
                _http,
                $"https://api.github.com/repos/{Owner}/{Repository}/attestations/sha256:{digest}",
                MaxAttestationResponseBytes,
                cancellationToken)
            .ConfigureAwait(false);

        var bundles = ParseBundles(attestationsJson);
        if (bundles.Count == 0)
        {
            throw new UpdatePackageVerificationException(
                $"No GitHub build-provenance attestation is published for {asset.Package.Name}.");
        }

        var policies = CreatePolicies(asset);

        foreach (var bundleJson in bundles)
        {
            SigstoreBundle bundle;
            try
            {
                bundle = SigstoreBundle.Deserialize(bundleJson);
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                continue;
            }

            await using var artifact = File.OpenRead(packagePath);
            foreach (var policy in policies)
            {
                artifact.Position = 0;
                var (verified, result) = await _sigstore
                    .TryVerifyStreamAsync(artifact, bundle, policy, cancellationToken)
                    .ConfigureAwait(false);
                if (verified)
                {
                    _log.LogInformation(
                        "Verified the build-provenance attestation for {Package} ({Version}).",
                        asset.Package.Name,
                        _productVersion);
                    return;
                }

                _log.LogDebug(
                    "An attestation for {Package} did not match the expected GitHub Actions build identity: {Reason}",
                    asset.Package.Name,
                    result?.FailureReason);
            }
        }

        throw new UpdatePackageVerificationException(
            $"No GitHub build-provenance attestation for {asset.Package.Name} could be verified against this repository's release workflow.");
    }

    /// <summary>
    /// Pins the attestation to this exact repository, the GitHub Actions OIDC
    /// issuer, and the exact release workflow file - not merely "some workflow
    /// in this repo" - so a compromised, unrelated workflow in the same
    /// repository could not forge a trusted update. The release workflow can
    /// legitimately produce a build from two different refs (see
    /// <c>.github/workflows/release.yml</c>): a <c>push</c> to the release tag
    /// itself, whose provenance's Build Config URI is pinned to
    /// <c>refs/tags/&lt;tag&gt;</c>; or a manual <c>workflow_dispatch</c> run
    /// (used to re-publish an existing tag), whose provenance instead reflects
    /// the ref the workflow *run* started from - the repository's default
    /// branch, since that is where the workflow is dispatched from. Both are
    /// accepted here without weakening the repository/workflow binding; a
    /// bundle must still match one of these exact refs.
    /// </summary>
    private static IReadOnlyList<VerificationPolicy> CreatePolicies(SelectedUpdateAsset asset)
    {
        var tag = ExtractTag(asset.Package.Name);
        var candidateRefs = new List<string> { $"refs/heads/{DefaultBranch}" };
        if (tag is not null) candidateRefs.Insert(0, $"refs/tags/{tag}");

        var policies = new List<VerificationPolicy>(candidateRefs.Count);
        foreach (var gitRef in candidateRefs)
        {
            var identity = CertificateIdentity.ForGitHubActions(Owner, Repository);
            identity.Extensions = new CertificateExtensionPolicy
            {
                BuildConfigUri = $"https://github.com/{Owner}/{Repository}/{WorkflowFileName}@{gitRef}"
            };
            policies.Add(new VerificationPolicy { CertificateIdentity = identity });
        }

        return policies;
    }

    /// <summary>Extracts "v1.2.3" from "WindowsCompanion-1.2.3-win-x64-setup.zip".</summary>
    private static string? ExtractTag(string packageName)
    {
        const string prefix = "WindowsCompanion-";
        if (!packageName.StartsWith(prefix, StringComparison.Ordinal)) return null;

        var rest = packageName[prefix.Length..];
        var dashIndex = rest.IndexOf("-win-", StringComparison.Ordinal);
        return dashIndex <= 0 ? null : $"v{rest[..dashIndex]}";
    }

    internal static string? ParseChecksumSidecar(string sidecarText, string expectedFileName)
    {
        // Standard `sha256sum` format: "<hash>  <filename>" (one or more spaces).
        foreach (var rawLine in sidecarText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            var separator = line.IndexOfAny([' ', '\t']);
            var hash = separator < 0 ? line : line[..separator];
            var name = separator < 0 ? null : line[separator..].TrimStart(' ', '*', '\t');

            if (hash.Length != 64 || !IsHex(hash)) continue;
            if (name is not null
                && !name.Equals(expectedFileName, StringComparison.Ordinal))
            {
                continue;
            }

            return hash.ToLowerInvariant();
        }

        return null;
    }

    private static bool IsHex(string value)
    {
        foreach (var c in value)
        {
            if (!Uri.IsHexDigit(c)) return false;
        }

        return true;
    }

    private static List<string> ParseBundles(string attestationsJson)
    {
        var bundles = new List<string>();
        using var document = JsonDocument.Parse(attestationsJson);
        if (!document.RootElement.TryGetProperty("attestations", out var attestations)
            || attestations.ValueKind != JsonValueKind.Array)
        {
            return bundles;
        }

        foreach (var attestation in attestations.EnumerateArray())
        {
            if (attestation.TryGetProperty("bundle", out var bundle)
                && bundle.ValueKind == JsonValueKind.Object)
            {
                bundles.Add(bundle.GetRawText());
            }
        }

        return bundles;
    }

    private async Task<string> GetStringAsync(
        HttpClient http,
        string url,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(
            new ProductInfoHeaderValue("WindowsCompanion", _productVersion));
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");

        using var response = await http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength > maxBytes)
            throw new InvalidDataException($"The response from {url} was too large.");

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (bytes.Length + read > maxBytes)
                throw new InvalidDataException($"The response from {url} was too large.");
            await bytes.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        bytes.Position = 0;
        using var reader = new StreamReader(bytes, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }
}
