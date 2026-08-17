using WindowsCompanion.Core.Sensors;

namespace WindowsCompanion.Core.Tests;

/// <summary>
/// Selection, truncation and attribute rules for the Now Playing / Media
/// Playing sensors. All of it is deterministic Core logic, so it is verified
/// without a real media session.
/// </summary>
public class MediaSensorTests
{
    [Fact]
    public void No_active_session_reports_nothing_playing_with_no_attributes()
    {
        var snapshot = MediaSnapshot.Empty;

        Assert.Equal("Idle", MediaPlaybackFormatter.DescribeTitle(snapshot));
        Assert.Equal(MediaPlaybackFormatter.NothingPlaying, MediaPlaybackFormatter.DescribeTitle(snapshot));
        Assert.Null(MediaPlaybackFormatter.BuildAttributes(snapshot));
        Assert.False(MediaPlaybackFormatter.IsPlaying(snapshot));
    }

    [Fact]
    public void Title_is_preferred_over_the_app_name()
    {
        var snapshot = new MediaSnapshot("Track Title", "Some Artist", "Some Player", MediaPlaybackStatus.Playing);

        Assert.Equal("Track Title", MediaPlaybackFormatter.DescribeTitle(snapshot));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_title_falls_back_to_the_app_name(string? title)
    {
        var snapshot = new MediaSnapshot(title, "Some Artist", "Some Player", MediaPlaybackStatus.Playing);

        Assert.Equal("Some Player", MediaPlaybackFormatter.DescribeTitle(snapshot));
    }

    [Fact]
    public void Missing_title_and_app_name_reports_nothing_playing()
    {
        var snapshot = new MediaSnapshot(null, "Some Artist", null, MediaPlaybackStatus.Playing);

        Assert.Equal(MediaPlaybackFormatter.NothingPlaying, MediaPlaybackFormatter.DescribeTitle(snapshot));
    }

    [Fact]
    public void Title_is_bounded_so_a_verbose_track_name_cannot_flood_the_state()
    {
        var snapshot = new MediaSnapshot(new string('A', 400), null, null, MediaPlaybackStatus.Playing);

        var title = MediaPlaybackFormatter.DescribeTitle(snapshot);

        Assert.Equal(MediaPlaybackFormatter.MaxStateLength, title.Length);
    }

    [Theory]
    [InlineData(MediaPlaybackStatus.Playing, true)]
    [InlineData(MediaPlaybackStatus.Paused, false)]
    [InlineData(MediaPlaybackStatus.Stopped, false)]
    [InlineData(MediaPlaybackStatus.Opened, false)]
    [InlineData(MediaPlaybackStatus.Changing, false)]
    [InlineData(MediaPlaybackStatus.Closed, false)]
    public void IsPlaying_is_true_only_for_the_playing_status(MediaPlaybackStatus status, bool expected)
    {
        var snapshot = new MediaSnapshot("Title", null, null, status);

        Assert.Equal(expected, MediaPlaybackFormatter.IsPlaying(snapshot));
    }

    [Fact]
    public void Attributes_include_artist_app_name_and_playback_status()
    {
        var snapshot = new MediaSnapshot("Title", "Some Artist", "Some Player", MediaPlaybackStatus.Paused);

        var attributes = MediaPlaybackFormatter.BuildAttributes(snapshot);

        Assert.NotNull(attributes);
        Assert.Equal("Some Artist", attributes!["artist"]);
        Assert.Equal("Some Player", attributes["app_name"]);
        Assert.Equal("Paused", attributes["playback_status"]);
    }

    [Fact]
    public void Attributes_omit_artist_and_app_name_when_unavailable_but_still_report_status()
    {
        var snapshot = new MediaSnapshot("Title", null, null, MediaPlaybackStatus.Playing);

        var attributes = MediaPlaybackFormatter.BuildAttributes(snapshot);

        Assert.NotNull(attributes);
        Assert.False(attributes!.ContainsKey("artist"));
        Assert.False(attributes.ContainsKey("app_name"));
        Assert.Equal("Playing", attributes["playback_status"]);
    }

    [Fact]
    public void Attributes_bound_artist_and_app_name_to_the_max_state_length()
    {
        var overlong = new string('a', 400);
        var snapshot = new MediaSnapshot("Title", overlong, overlong, MediaPlaybackStatus.Playing);

        var attributes = MediaPlaybackFormatter.BuildAttributes(snapshot);

        Assert.NotNull(attributes);
        Assert.Equal(255, ((string)attributes!["artist"]).Length);
        Assert.Equal(255, ((string)attributes["app_name"]).Length);
    }
}

/// <summary>
/// <see cref="MediaSessionSelector"/> covers the multi-session regression:
/// a "current" session that Windows reports as paused must not win over a
/// different session that is genuinely playing.
/// </summary>
public class MediaSessionSelectorTests
{
    private readonly record struct FakeSession(string Id, MediaPlaybackStatus Status);

    [Fact]
    public void A_playing_alternate_is_preferred_over_a_paused_current_session()
    {
        var candidates = new[]
        {
            new FakeSession("current-paused", MediaPlaybackStatus.Paused),
            new FakeSession("alternate-playing", MediaPlaybackStatus.Playing)
        };

        var selected = MediaSessionSelector.SelectPlaying(candidates, s => s.Status);

        Assert.Equal("alternate-playing", selected.Id);
    }

    [Fact]
    public void The_first_playing_candidate_wins_when_several_are_playing()
    {
        var candidates = new[]
        {
            new FakeSession("first-playing", MediaPlaybackStatus.Playing),
            new FakeSession("second-playing", MediaPlaybackStatus.Playing)
        };

        var selected = MediaSessionSelector.SelectPlaying(candidates, s => s.Status);

        Assert.Equal("first-playing", selected.Id);
    }

    [Fact]
    public void No_playing_candidate_falls_back_to_the_default_for_the_caller_to_handle()
    {
        var candidates = new[]
        {
            new FakeSession("current-paused", MediaPlaybackStatus.Paused),
            new FakeSession("closed", MediaPlaybackStatus.Closed)
        };

        var selected = MediaSessionSelector.SelectPlaying(candidates, s => s.Status);

        Assert.Null(selected.Id);
    }

    [Fact]
    public void An_empty_candidate_set_falls_back_to_the_default()
    {
        var selected = MediaSessionSelector.SelectPlaying(
            Array.Empty<FakeSession>(), s => s.Status);

        Assert.Null(selected.Id);
    }
}
