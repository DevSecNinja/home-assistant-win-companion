namespace WindowsCompanion.Core.Sensors;

/// <summary>
/// Mirrors the Windows System Media Transport Controls (SMTC)
/// <c>GlobalSystemMediaTransportControlsSessionPlaybackStatus</c> enum, so the App
/// layer can hand the raw OS value straight to Core without a translation table.
/// </summary>
public enum MediaPlaybackStatus
{
    Closed = 0,
    Opened = 1,
    Changing = 2,
    Stopped = 3,
    Playing = 4,
    Paused = 5
}

/// <summary>
/// A point-in-time reading of the active SMTC session, or the default value
/// when there is none.
/// </summary>
public readonly record struct MediaSnapshot(
    string? Title,
    string? Artist,
    string? AppName,
    MediaPlaybackStatus Status)
{
    public static MediaSnapshot Empty { get; } = new(null, null, null, MediaPlaybackStatus.Closed);
}

/// <summary>
/// Selection, truncation and attribute rules for the Now Playing / Media Playing
/// sensors. All of it is deterministic Core logic so it is verified without a
/// real media session.
/// </summary>
public static class MediaPlaybackFormatter
{
    public const int MaxStateLength = 255;

    public const string NothingPlaying = "Nothing Playing";

    /// <summary>
    /// The Now Playing sensor's state: the track title, falling back to the app
    /// name when the title is unavailable, and finally to a fixed placeholder
    /// when there is no active session at all.
    /// </summary>
    public static string DescribeTitle(MediaSnapshot snapshot)
    {
        var value = snapshot.Title;
        if (string.IsNullOrWhiteSpace(value))
            value = snapshot.AppName;
        if (string.IsNullOrWhiteSpace(value))
            return NothingPlaying;

        return value.Length <= MaxStateLength
            ? value
            : value[..MaxStateLength];
    }

    /// <summary>Whether the Media Playing binary_sensor should report on.</summary>
    public static bool IsPlaying(MediaSnapshot snapshot) =>
        snapshot.Status == MediaPlaybackStatus.Playing;

    public static string DescribeStatus(MediaPlaybackStatus status) => status switch
    {
        MediaPlaybackStatus.Opened => "Opened",
        MediaPlaybackStatus.Changing => "Changing",
        MediaPlaybackStatus.Stopped => "Stopped",
        MediaPlaybackStatus.Playing => "Playing",
        MediaPlaybackStatus.Paused => "Paused",
        _ => "Closed"
    };

    /// <summary>
    /// Attributes for the Now Playing sensor. <c>null</c> when there is nothing
    /// playing, so a closed session reports a bare state instead of a stale
    /// artist/app_name pair.
    /// </summary>
    public static IDictionary<string, object>? BuildAttributes(MediaSnapshot snapshot)
    {
        if (snapshot.Status == MediaPlaybackStatus.Closed
            && string.IsNullOrWhiteSpace(snapshot.Title)
            && string.IsNullOrWhiteSpace(snapshot.Artist)
            && string.IsNullOrWhiteSpace(snapshot.AppName))
        {
            return null;
        }

        var attributes = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["playback_status"] = DescribeStatus(snapshot.Status)
        };

        if (!string.IsNullOrWhiteSpace(snapshot.Artist))
            attributes["artist"] = Bound(snapshot.Artist);
        if (!string.IsNullOrWhiteSpace(snapshot.AppName))
            attributes["app_name"] = Bound(snapshot.AppName);

        return attributes;
    }

    /// <summary>
    /// Attributes are not subject to Home Assistant's 255-character state
    /// limit, but a misbehaving or malicious media source could still stuff
    /// an unbounded artist/app name into the payload. Bound it to the same
    /// length as the state so a single sensor cannot balloon the webhook
    /// payload or the recorder.
    /// </summary>
    private static string Bound(string value) =>
        value.Length <= MaxStateLength ? value : value[..MaxStateLength];
}

/// <summary>
/// Picks which SMTC session to read from a set of candidates, independent of
/// the WinRT session type itself, so the policy is unit-testable in Core.
/// </summary>
/// <remarks>
/// Windows' own "current" session is often just the most recently activated
/// one, not the one actually making sound - a paused player can outrank a
/// playing one. Preferring whichever candidate is actively <c>Playing</c>
/// over an arbitrary "current" fallback is the fix for that: a paused
/// Spotify session can no longer mask media genuinely playing in, say, a
/// browser tab.
/// </remarks>
public static class MediaSessionSelector
{
    /// <summary>
    /// Returns the first candidate reporting <see cref="MediaPlaybackStatus.Playing"/>,
    /// or <c>default</c> when none are - the caller is expected to fall back to
    /// its own notion of "the current session" in that case.
    /// </summary>
    public static T? SelectPlaying<T>(IEnumerable<T> candidates, Func<T, MediaPlaybackStatus> statusOf)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(statusOf);

        foreach (var candidate in candidates)
        {
            if (statusOf(candidate) == MediaPlaybackStatus.Playing)
                return candidate;
        }

        return default;
    }
}
