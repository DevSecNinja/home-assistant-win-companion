using HaCompanion.Core.Sensors;

namespace HaCompanion.Core.Tests;

public class FrontmostAppTests
{
    [Fact]
    public void Application_name_is_the_privacy_safe_default()
    {
        var preferences = new SensorPreferences();
        var snapshot = new FrontmostAppSnapshot("chrome.exe", "Customer - confidential");

        Assert.Equal(FrontmostAppMode.ApplicationName, preferences.FrontmostAppMode);
        Assert.Equal("chrome.exe", FrontmostAppState.Select(snapshot, preferences.FrontmostAppMode));
    }

    [Fact]
    public void Full_title_requires_the_explicit_mode()
    {
        var snapshot = new FrontmostAppSnapshot("chrome.exe", "Customer - confidential");

        Assert.Equal(
            "Customer - confidential",
            FrontmostAppState.Select(snapshot, FrontmostAppMode.FullWindowTitle));
    }

    [Fact]
    public void Full_title_is_truncated_without_an_attribute_escape_hatch()
    {
        var value = FrontmostAppState.Select(
            new FrontmostAppSnapshot("app.exe", new string('x', 400)),
            FrontmostAppMode.FullWindowTitle);

        Assert.Equal(255, value.Length);
    }

    [Fact]
    public void Missing_title_falls_back_to_application_name()
    {
        Assert.Equal(
            "app.exe",
            FrontmostAppState.Select(
                new FrontmostAppSnapshot("app.exe", null),
                FrontmostAppMode.FullWindowTitle));
    }

    [Fact]
    public void Rapid_stages_commit_only_the_latest_value()
    {
        var value = new DebouncedValue<string>();
        value.SetInitial("first");
        var stale = value.Stage("second");
        var latest = value.Stage("third");

        Assert.False(value.TryCommit(stale));
        Assert.True(value.TryCommit(latest));
        Assert.Equal("third", value.Current);
        Assert.False(value.TryCommit(latest));
    }

    [Fact]
    public void Invalidating_prevents_an_old_hook_generation_from_committing()
    {
        var value = new DebouncedValue<string>();
        value.SetInitial("first");
        var oldGeneration = value.Stage("private title");

        value.InvalidatePending();
        value.SetInitial("companion.exe");

        Assert.False(value.TryCommit(oldGeneration));
        Assert.Equal("companion.exe", value.Current);
    }
}
