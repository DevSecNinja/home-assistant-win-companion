using System.Text.Json;
using WindowsCompanion.Core.Updates;

namespace WindowsCompanion.Core.Tests;

public class StartupUpdateTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.4", -1)]
    [InlineData("v1.2.3", "1.2.3", 0)]
    [InlineData("V2.0.0+build.19", "2.0.0", 0)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11", -1)]
    [InlineData("1.0.0-beta.99999999999999999999", "1.0.0-beta.100000000000000000000", -1)]
    [InlineData("1.0.0-rc.1", "1.0.0", -1)]
    public void Semantic_versions_follow_release_precedence(
        string left, string right, int expectedSign)
    {
        Assert.True(SemanticVersion.TryParse(left, out var parsedLeft));
        Assert.True(SemanticVersion.TryParse(right, out var parsedRight));

        Assert.Equal(expectedSign, Math.Sign(parsedLeft!.CompareTo(parsedRight)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("1.02.3")]
    [InlineData("1.2.3-beta.01")]
    [InlineData("release-1.2.3")]
    [InlineData("1.2.3+")]
    public void Malformed_versions_are_rejected(string value) =>
        Assert.False(SemanticVersion.TryParse(value, out _));

    [Fact]
    public void Release_parser_requires_an_object_or_array()
    {
        Assert.Throws<JsonException>(() => ReleaseCatalogParser.Parse("42"));
    }

    [Fact]
    public void Release_parser_accepts_the_latest_release_object()
    {
        var releases = ReleaseCatalogParser.Parse(
            """
            {"tag_name":"v1.1.0","draft":false,"prerelease":false,"html_url":"https://github.com/DevSecNinja/home-assistant-win-companion/releases/tag/v1.1.0"}
            """);

        Assert.Equal("v1.1.0", Assert.Single(releases).TagName);
    }

    [Fact]
    public void Release_parser_skips_entries_missing_required_stability_fields()
    {
        var releases = ReleaseCatalogParser.Parse(
            """
            [
              {"tag_name":"v9.0.0","prerelease":false,"html_url":"https://github.com/DevSecNinja/home-assistant-win-companion/releases/tag/v9.0.0"},
              {"tag_name":"v8.0.0","draft":false,"html_url":"https://github.com/DevSecNinja/home-assistant-win-companion/releases/tag/v8.0.0"},
              {"tag_name":"v1.1.0","draft":false,"prerelease":false,"html_url":"https://github.com/DevSecNinja/home-assistant-win-companion/releases/tag/v1.1.0"}
            ]
            """);

        var release = Assert.Single(releases);
        Assert.Equal("v1.1.0", release.TagName);
    }

    [Fact]
    public void Only_the_highest_newer_stable_trusted_release_is_selected()
    {
        var installed = Parse("1.2.3");
        var releases = ReleaseCatalogParser.Parse(
            """
            [
              {"tag_name":"v9.0.0","draft":true,"prerelease":false,"html_url":"https://github.com/DevSecNinja/home-assistant-win-companion/releases/tag/v9.0.0"},
              {"tag_name":"v8.0.0","draft":false,"prerelease":true,"html_url":"https://github.com/DevSecNinja/home-assistant-win-companion/releases/tag/v8.0.0"},
              {"tag_name":"v7.0.0-beta.1","draft":false,"prerelease":false,"html_url":"https://github.com/DevSecNinja/home-assistant-win-companion/releases/tag/v7.0.0-beta.1"},
              {"tag_name":"not-a-version","draft":false,"prerelease":false,"html_url":"https://github.com/DevSecNinja/home-assistant-win-companion/releases/tag/not-a-version"},
              {"tag_name":"v6.0.0","draft":false,"prerelease":false,"html_url":"http://github.com/DevSecNinja/home-assistant-win-companion/releases/tag/v6.0.0"},
              {"tag_name":"v5.0.0","draft":false,"prerelease":false,"html_url":"https://example.invalid/DevSecNinja/home-assistant-win-companion/releases/tag/v5.0.0"},
              {"tag_name":"v4.0.0","draft":false,"prerelease":false,"html_url":"https://github.com/DevSecNinja/home-assistant-win-companion/releases/tag/v3.0.0"},
              {"tag_name":"v1.3.0","draft":false,"prerelease":false,"html_url":"https://github.com/DevSecNinja/home-assistant-win-companion/releases/tag/v1.3.0"},
              {"tag_name":"v1.4.0","draft":false,"prerelease":false,"html_url":"https://github.com/DevSecNinja/home-assistant-win-companion/releases/tag/v1.4.0"}
            ]
            """);

        var update = UpdatePolicy.FindUpdate(installed, releases);

        Assert.NotNull(update);
        Assert.Equal("1.4.0", update.AvailableVersion.ToString());
        Assert.Equal(
            "https://github.com/DevSecNinja/home-assistant-win-companion/releases/tag/v1.4.0",
            update.ReleasePage.AbsoluteUri);
    }

    [Theory]
    [InlineData("1.4.0")]
    [InlineData("2.0.0")]
    public void Current_or_newer_installs_do_not_notify(string installedVersion)
    {
        var update = UpdatePolicy.FindUpdate(
            Parse(installedVersion),
            [Release("v1.4.0")]);

        Assert.Null(update);
    }

    [Fact]
    public async Task The_startup_check_suppresses_duplicate_attempts_and_notifications()
    {
        var source = new BlockingReleaseSource([Release("v2.0.0")]);
        var notifications = new RecordingNotifications();
        var checker = new StartupUpdateChecker(source, notifications);

        var first = checker.CheckOnceAsync(Parse("1.0.0"));
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var duplicate = await checker.CheckOnceAsync(Parse("1.0.0"));
        source.Release();

        Assert.True(await first);
        Assert.False(duplicate);
        Assert.Equal(1, source.CallCount);
        Assert.Single(notifications.Updates);
    }

    [Fact]
    public async Task No_newer_release_produces_no_notification()
    {
        var source = new BlockingReleaseSource([Release("v1.0.0")], blocked: false);
        var notifications = new RecordingNotifications();
        var checker = new StartupUpdateChecker(source, notifications);

        Assert.False(await checker.CheckOnceAsync(Parse("1.0.0")));
        Assert.Empty(notifications.Updates);
    }

    private static SemanticVersion Parse(string value)
    {
        Assert.True(SemanticVersion.TryParse(value, out var version));
        return version!;
    }

    private static ReleaseCandidate Release(string tag) =>
        new(tag, false, false,
            $"https://github.com/DevSecNinja/home-assistant-win-companion/releases/tag/{tag}");

    private sealed class BlockingReleaseSource(
        IReadOnlyList<ReleaseCandidate> releases,
        bool blocked = true) : IReleaseSource
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly bool _blocked = blocked;

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }

        public async Task<IReadOnlyList<ReleaseCandidate>> GetReleasesAsync(
            CancellationToken cancellationToken)
        {
            CallCount++;
            Started.TrySetResult();
            if (_blocked) await _release.Task.WaitAsync(cancellationToken);
            return releases;
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class RecordingNotifications : IUpdateNotificationSink
    {
        public List<AvailableUpdate> Updates { get; } = [];

        public void Show(AvailableUpdate update) => Updates.Add(update);
    }
}
