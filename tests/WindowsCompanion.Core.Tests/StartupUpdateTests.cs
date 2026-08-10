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
    public void The_checker_starts_idle()
    {
        var checker = Checker(
            new ScriptedReleaseSource(_ => Task.FromResult(Releases("v1.0.0"))));

        Assert.Equal(UpdateCheckStatus.Idle, checker.State.Status);
        Assert.Equal("1.0.0", checker.State.InstalledVersion.ToString());
    }

    [Fact]
    public async Task A_check_transitions_through_checking_to_current()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new ScriptedReleaseSource(async cancellationToken =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return Releases("v1.0.0");
        });
        var checker = Checker(source);
        var states = new List<UpdateCheckState>();
        checker.StateChanged += states.Add;

        var check = checker.CheckAsync(UpdateCheckTrigger.User);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(UpdateCheckStatus.Checking, checker.State.Status);
        release.TrySetResult();
        var result = await check;

        Assert.Equal(UpdateCheckStatus.Current, result.Status);
        Assert.Equal(
            [UpdateCheckStatus.Checking, UpdateCheckStatus.Current],
            states.Select(state => state.Status));
    }

    [Fact]
    public async Task A_newer_release_transitions_to_available_and_notifies_once()
    {
        var source = new ScriptedReleaseSource(
            _ => Task.FromResult(Releases("v2.0.0")),
            _ => Task.FromResult(Releases("v2.0.0")));
        var notifications = new RecordingNotifications();
        var checker = Checker(source, notifications);

        var first = await checker.CheckAsync(UpdateCheckTrigger.Automatic);
        var second = await checker.CheckAsync(UpdateCheckTrigger.User);

        Assert.Equal(UpdateCheckStatus.Available, first.Status);
        Assert.Equal(UpdateCheckStatus.Available, second.Status);
        Assert.Equal("2.0.0", second.AvailableUpdate?.AvailableVersion.ToString());
        Assert.Single(notifications.Updates);
    }

    [Fact]
    public async Task Each_available_version_notifies_at_most_once_per_process()
    {
        var source = new ScriptedReleaseSource(
            _ => Task.FromResult(Releases("v2.0.0")),
            _ => Task.FromResult(Releases("v3.0.0")),
            _ => Task.FromResult(Releases("v2.0.0")));
        var notifications = new RecordingNotifications();
        var checker = Checker(source, notifications);

        await checker.CheckAsync(UpdateCheckTrigger.User);
        await checker.CheckAsync(UpdateCheckTrigger.User);
        await checker.CheckAsync(UpdateCheckTrigger.User);

        Assert.Equal(
            ["2.0.0", "3.0.0"],
            notifications.Updates.Select(update => update.AvailableVersion.ToString()));
    }

    [Fact]
    public async Task A_failure_is_nonfatal_and_preserves_a_known_update()
    {
        var source = new ScriptedReleaseSource(
            _ => Task.FromResult(Releases("v2.0.0")),
            _ => Task.FromException<IReadOnlyList<ReleaseCandidate>>(
                new HttpRequestException("offline")));
        var checker = Checker(source);

        await checker.CheckAsync(UpdateCheckTrigger.Automatic);
        await Assert.ThrowsAsync<HttpRequestException>(
            () => checker.CheckAsync(UpdateCheckTrigger.User));

        Assert.Equal(UpdateCheckStatus.Error, checker.State.Status);
        Assert.Equal("2.0.0", checker.State.AvailableUpdate?.AvailableVersion.ToString());
        Assert.Contains("try again", checker.State.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_failed_recheck_preserves_the_latest_known_stable_version()
    {
        var source = new ScriptedReleaseSource(
            _ => Task.FromResult(Releases("v1.0.0")),
            _ => Task.FromException<IReadOnlyList<ReleaseCandidate>>(
                new HttpRequestException("offline")));
        var checker = Checker(source);

        await checker.CheckAsync(UpdateCheckTrigger.Automatic);
        await Assert.ThrowsAsync<HttpRequestException>(
            () => checker.CheckAsync(UpdateCheckTrigger.User));

        Assert.Equal(
            "1.0.0",
            checker.State.LatestKnownStableVersion?.ToString());
    }

    [Fact]
    public async Task A_new_check_cancels_the_old_one_and_cannot_publish_a_stale_result()
    {
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new ScriptedReleaseSource(
            async _ =>
            {
                firstStarted.TrySetResult();
                await releaseFirst.Task;
                return Releases("v9.0.0");
            },
            _ => Task.FromResult(Releases("v2.0.0")));
        var notifications = new RecordingNotifications();
        var checker = Checker(source, notifications);
        var availableVersions = new List<string>();
        checker.StateChanged += state =>
        {
            if (state.Status == UpdateCheckStatus.Available)
                availableVersions.Add(state.AvailableUpdate!.AvailableVersion.ToString());
        };

        var first = checker.CheckAsync(UpdateCheckTrigger.User);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = checker.CheckAsync(UpdateCheckTrigger.User);

        Assert.Equal(1, source.CallCount);
        releaseFirst.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var result = await second;

        Assert.Equal(2, source.CallCount);
        Assert.Equal(1, source.MaxConcurrentCalls);
        Assert.Equal("2.0.0", result.AvailableUpdate?.AvailableVersion.ToString());
        Assert.Equal(["2.0.0"], availableVersions);
        Assert.Equal("2.0.0", Assert.Single(notifications.Updates).AvailableVersion.ToString());
    }

    [Fact]
    public async Task Superseding_a_published_result_before_notification_suppresses_its_toast()
    {
        var source = new ScriptedReleaseSource(
            _ => Task.FromResult(Releases("v2.0.0")),
            _ => Task.FromResult(Releases("v1.0.0")));
        var notifications = new RecordingNotifications();
        var checker = Checker(source, notifications);
        var publishing = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var continuePublishing = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        checker.StateChanged += state =>
        {
            if (state.Status != UpdateCheckStatus.Available) return;
            publishing.TrySetResult();
            continuePublishing.Task.GetAwaiter().GetResult();
        };

        var first = Task.Run(() => checker.CheckAsync(UpdateCheckTrigger.User));
        await publishing.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = checker.CheckAsync(UpdateCheckTrigger.User);
        continuePublishing.TrySetResult();

        Assert.Equal(UpdateCheckStatus.Available, (await first).Status);
        Assert.Equal(UpdateCheckStatus.Current, (await second).Status);
        Assert.Empty(notifications.Updates);
        Assert.Equal(UpdateCheckStatus.Current, checker.State.Status);
    }

    private static SemanticVersion Parse(string value)
    {
        Assert.True(SemanticVersion.TryParse(value, out var version));
        return version!;
    }

    private static ReleaseCandidate Release(string tag) =>
        new(tag, false, false,
            $"https://github.com/DevSecNinja/home-assistant-win-companion/releases/tag/{tag}");

    private static StartupUpdateChecker Checker(
        IReleaseSource source,
        RecordingNotifications? notifications = null) =>
        new(Parse("1.0.0"), source, notifications ?? new RecordingNotifications());

    private static IReadOnlyList<ReleaseCandidate> Releases(params string[] tags) =>
        tags.Select(Release).ToArray();

    private sealed class ScriptedReleaseSource(
        params Func<CancellationToken, Task<IReadOnlyList<ReleaseCandidate>>>[] steps)
        : IReleaseSource
    {
        private int _activeCalls;
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);
        public int MaxConcurrentCalls { get; private set; }

        public async Task<IReadOnlyList<ReleaseCandidate>> GetReleasesAsync(
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _callCount);
            var active = Interlocked.Increment(ref _activeCalls);
            MaxConcurrentCalls = Math.Max(MaxConcurrentCalls, active);
            try
            {
                return await steps[call - 1](cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }
    }

    private sealed class RecordingNotifications : IUpdateNotificationSink
    {
        public List<AvailableUpdate> Updates { get; } = [];

        public void Show(AvailableUpdate update) => Updates.Add(update);
    }
}
