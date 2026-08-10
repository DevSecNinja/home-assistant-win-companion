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

public enum UpdateCheckStatus
{
    Idle,
    Checking,
    Current,
    Available,
    Error
}

public enum UpdateCheckTrigger
{
    Automatic,
    User
}

/// <summary>The latest process-wide update-check result.</summary>
public sealed record UpdateCheckState(
    UpdateCheckStatus Status,
    UpdateCheckTrigger Trigger,
    SemanticVersion InstalledVersion,
    AvailableUpdate? AvailableUpdate = null,
    string? ErrorMessage = null,
    long Revision = 0);

/// <summary>
/// Serializes update checks, cancels superseded work, and publishes only the
/// newest result.
/// </summary>
public sealed class StartupUpdateChecker
{
    private const string FailureMessage =
        "The update check failed. Check your internet connection and try again.";

    private readonly IReleaseSource _releases;
    private readonly IUpdateNotificationSink _notifications;
    private readonly SemanticVersion _installedVersion;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _singleFlight = new(1, 1);
    private readonly HashSet<string> _notifiedVersions = new(StringComparer.Ordinal);
    private CancellationTokenSource? _activeCheck;
    private UpdateCheckState _state;
    private long _revision;

    public StartupUpdateChecker(
        SemanticVersion installedVersion,
        IReleaseSource releases,
        IUpdateNotificationSink notifications)
    {
        _installedVersion = installedVersion
            ?? throw new ArgumentNullException(nameof(installedVersion));
        _releases = releases ?? throw new ArgumentNullException(nameof(releases));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _state = new(
            UpdateCheckStatus.Idle,
            UpdateCheckTrigger.Automatic,
            _installedVersion);
    }

    public UpdateCheckState State
    {
        get
        {
            lock (_gate) return _state;
        }
    }

    public event Action<UpdateCheckState>? StateChanged;

    public async Task<UpdateCheckState> CheckAsync(
        UpdateCheckTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        CancellationTokenSource checkCancellation;
        long revision;
        AvailableUpdate? knownUpdate;
        lock (_gate)
        {
            revision = ++_revision;
            _activeCheck?.Cancel();
            checkCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeCheck = checkCancellation;
            knownUpdate = _state.AvailableUpdate;
        }

        PublishIfCurrent(
            revision,
            checkCancellation,
            new(
                UpdateCheckStatus.Checking,
                trigger,
                _installedVersion,
                knownUpdate,
                Revision: revision));

        var entered = false;
        try
        {
            await _singleFlight
                .WaitAsync(checkCancellation.Token)
                .ConfigureAwait(false);
            entered = true;

            var releases = await _releases
                .GetReleasesAsync(checkCancellation.Token)
                .ConfigureAwait(false);
            checkCancellation.Token.ThrowIfCancellationRequested();

            var update = UpdatePolicy.FindUpdate(_installedVersion, releases);
            var result = new UpdateCheckState(
                update is null ? UpdateCheckStatus.Current : UpdateCheckStatus.Available,
                trigger,
                _installedVersion,
                update,
                Revision: revision);
            if (!PublishIfCurrent(revision, checkCancellation, result))
                throw new OperationCanceledException(checkCancellation.Token);

            if (update is not null)
                NotifyIfCurrent(revision, checkCancellation, update);

            return result;
        }
        catch (OperationCanceledException) when (checkCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            PublishIfCurrent(
                revision,
                checkCancellation,
                new(
                    UpdateCheckStatus.Error,
                    trigger,
                    _installedVersion,
                    knownUpdate,
                    FailureMessage,
                    revision));
            throw;
        }
        finally
        {
            if (entered) _singleFlight.Release();
            lock (_gate)
            {
                if (ReferenceEquals(_activeCheck, checkCancellation))
                    _activeCheck = null;
            }
            checkCancellation.Dispose();
        }
    }

    public async Task CancelAsync()
    {
        lock (_gate)
        {
            _revision++;
            _activeCheck?.Cancel();
        }

        await _singleFlight.WaitAsync().ConfigureAwait(false);
        _singleFlight.Release();
    }

    private bool PublishIfCurrent(
        long revision,
        CancellationTokenSource checkCancellation,
        UpdateCheckState state)
    {
        Action<UpdateCheckState>? changed;
        lock (_gate)
        {
            if (revision != _revision || checkCancellation.IsCancellationRequested)
                return false;

            _state = state;
            changed = StateChanged;
        }

        changed?.Invoke(state);
        return true;
    }

    private void NotifyIfCurrent(
        long revision,
        CancellationTokenSource checkCancellation,
        AvailableUpdate update)
    {
        lock (_gate)
        {
            if (revision != _revision || checkCancellation.IsCancellationRequested)
                return;

            if (_notifiedVersions.Add(update.AvailableVersion.ToString()))
                _notifications.Show(update);
        }
    }
}
