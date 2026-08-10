using System.Text.Json;

namespace WindowsCompanion.Core.Updates;

/// <summary>Release fields needed to decide whether an update is available.</summary>
public sealed record ReleaseCandidate(
    string TagName,
    bool IsDraft,
    bool IsPreRelease,
    string PageUrl);

/// <summary>A newer trusted stable release that may be presented to the user.</summary>
public sealed record AvailableUpdate(
    SemanticVersion InstalledVersion,
    SemanticVersion AvailableVersion,
    Uri ReleasePage);

/// <summary>Parses the public GitHub releases response without making network calls.</summary>
public static class ReleaseCatalogParser
{
    public static IReadOnlyList<ReleaseCandidate> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var releases = new List<ReleaseCandidate>();
        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            if (!TryAdd(document.RootElement, releases))
                throw new JsonException("The GitHub release object was missing required fields.");
        }
        else if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in document.RootElement.EnumerateArray())
                TryAdd(item, releases);
        }
        else
        {
            throw new JsonException("The GitHub release response was not an object or array.");
        }

        return releases;
    }

    private static bool TryAdd(JsonElement item, ICollection<ReleaseCandidate> releases)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !TryString(item, "tag_name", out var tag)
            || !TryBoolean(item, "draft", out var draft)
            || !TryBoolean(item, "prerelease", out var preRelease)
            || !TryString(item, "html_url", out var page))
        {
            return false;
        }

        releases.Add(new ReleaseCandidate(tag!, draft, preRelease, page!));
        return true;
    }

    private static bool TryString(JsonElement item, string property, out string? result)
    {
        result = null;
        if (!item.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        result = value.GetString();
        return !string.IsNullOrWhiteSpace(result);
    }

    private static bool TryBoolean(JsonElement item, string property, out bool result)
    {
        result = false;
        if (!item.TryGetProperty(property, out var value)
            || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        result = value.GetBoolean();
        return true;
    }
}

/// <summary>Filters release metadata and selects the highest trusted stable version.</summary>
public static class UpdatePolicy
{
    private const string ReleasePathPrefix =
        "/DevSecNinja/home-assistant-win-companion/releases/tag/";

    public static AvailableUpdate? FindUpdate(
        SemanticVersion installed,
        IEnumerable<ReleaseCandidate> releases)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(releases);

        AvailableUpdate? selected = null;
        foreach (var release in releases)
        {
            if (release.IsDraft || release.IsPreRelease
                || !SemanticVersion.TryParse(release.TagName, out var version)
                || version!.IsPreRelease
                || version <= installed
                || !TryCreateReleasePage(release.PageUrl, release.TagName, out var page))
            {
                continue;
            }

            if (selected is null || version > selected.AvailableVersion)
                selected = new AvailableUpdate(installed, version, page!);
        }

        return selected;
    }

    private static bool TryCreateReleasePage(string value, string tagName, out Uri? page)
    {
        page = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate)
            || candidate.Scheme != Uri.UriSchemeHttps
            || !candidate.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !candidate.IsDefaultPort
            || !string.IsNullOrEmpty(candidate.UserInfo)
            || !string.IsNullOrEmpty(candidate.Query)
            || !string.IsNullOrEmpty(candidate.Fragment)
            || !candidate.AbsolutePath.StartsWith(ReleasePathPrefix, StringComparison.OrdinalIgnoreCase)
            || !Uri.UnescapeDataString(candidate.AbsolutePath[ReleasePathPrefix.Length..])
                .Equals(tagName, StringComparison.Ordinal))
        {
            return false;
        }

        page = candidate;
        return true;
    }
}

/// <summary>Retrieves release metadata from a platform integration.</summary>
public interface IReleaseSource
{
    Task<IReadOnlyList<ReleaseCandidate>> GetReleasesAsync(CancellationToken cancellationToken);
}

/// <summary>Presents an available update through a platform integration.</summary>
public interface IUpdateNotificationSink
{
    void Show(AvailableUpdate update);
}

/// <summary>Runs at most one release lookup and one notification per process instance.</summary>
public sealed class StartupUpdateChecker
{
    private readonly IReleaseSource _releases;
    private readonly IUpdateNotificationSink _notifications;
    private int _started;

    public StartupUpdateChecker(IReleaseSource releases, IUpdateNotificationSink notifications)
    {
        _releases = releases ?? throw new ArgumentNullException(nameof(releases));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
    }

    public async Task<bool> CheckOnceAsync(
        SemanticVersion installedVersion,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return false;

        var releases = await _releases
            .GetReleasesAsync(cancellationToken)
            .ConfigureAwait(false);
        var update = UpdatePolicy.FindUpdate(installedVersion, releases);
        if (update is null) return false;

        _notifications.Show(update);
        return true;
    }
}
