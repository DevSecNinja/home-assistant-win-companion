using WindowsCompanion.Core.Updates;
using WindowsCompanion_App.Services;

namespace WindowsCompanion.App.Tests;

public class UpdateUiTests
{
    [Theory]
    [InlineData(UpdateCheckStatus.Idle, UpdateCheckTrigger.Automatic, false, false)]
    [InlineData(UpdateCheckStatus.Checking, UpdateCheckTrigger.User, true, false)]
    [InlineData(UpdateCheckStatus.Current, UpdateCheckTrigger.User, true, true)]
    [InlineData(UpdateCheckStatus.Error, UpdateCheckTrigger.User, true, true)]
    public void No_known_update_uses_the_check_action(
        UpdateCheckStatus status,
        UpdateCheckTrigger trigger,
        bool bannerOpen,
        bool recheckEnabled)
    {
        var presentation = UpdateStatusPresentation.Create(
            State(status, trigger, error: status == UpdateCheckStatus.Error));

        Assert.Equal("Check for updates…", presentation.TrayActionLabel);
        Assert.Equal(bannerOpen, presentation.IsBannerOpen);
        Assert.False(presentation.IsReleaseActionVisible);
        Assert.Equal(status != UpdateCheckStatus.Idle, presentation.IsRecheckVisible);
        Assert.Equal(recheckEnabled, presentation.IsRecheckEnabled);
        Assert.Equal("1.0.0", presentation.InstalledVersionText);
    }

    [Fact]
    public void Checking_is_shown_before_a_user_check_completes()
    {
        var presentation = UpdateStatusPresentation.Create(
            State(UpdateCheckStatus.Checking, UpdateCheckTrigger.User));

        Assert.Equal("Checking for updates…", presentation.BannerTitle);
        Assert.True(presentation.IsBannerOpen);
        Assert.False(presentation.IsRecheckEnabled);
    }

    [Fact]
    public void Automatic_current_and_error_results_do_not_create_banner_spam()
    {
        var current = UpdateStatusPresentation.Create(
            State(UpdateCheckStatus.Current, UpdateCheckTrigger.Automatic));
        var error = UpdateStatusPresentation.Create(
            State(UpdateCheckStatus.Error, UpdateCheckTrigger.Automatic, error: true));

        Assert.False(current.IsBannerOpen);
        Assert.False(error.IsBannerOpen);
    }

    [Fact]
    public void A_known_update_uses_install_and_exposes_release_and_recheck_actions()
    {
        var presentation = UpdateStatusPresentation.Create(
            State(
                UpdateCheckStatus.Available,
                UpdateCheckTrigger.Automatic,
                Available()));

        Assert.Equal("Install update…", presentation.TrayActionLabel);
        Assert.True(presentation.IsBannerOpen);
        Assert.True(presentation.IsReleaseActionVisible);
        Assert.True(presentation.IsRecheckVisible);
        Assert.True(presentation.IsRecheckEnabled);
        Assert.Equal("2.0.0", presentation.LatestVersionText);
        Assert.Equal("Version 2.0.0 is available.", presentation.SettingsStatusText);
        Assert.Equal("Recheck for updates", presentation.SettingsCheckLabel);
        Assert.True(presentation.IsSettingsInstallVisible);
    }

    [Fact]
    public void Current_release_is_the_latest_known_stable_version()
    {
        var presentation = UpdateStatusPresentation.Create(
            State(UpdateCheckStatus.Current, UpdateCheckTrigger.User));

        Assert.Equal("1.0.0", presentation.InstalledVersionText);
        Assert.Equal("1.0.0", presentation.LatestVersionText);
        Assert.Equal("You're up to date.", presentation.SettingsStatusText);
        Assert.False(presentation.IsSettingsInstallVisible);
    }

    [Fact]
    public void Failed_recheck_keeps_the_latest_known_stable_version()
    {
        var presentation = UpdateStatusPresentation.Create(
            State(UpdateCheckStatus.Error, UpdateCheckTrigger.User) with
            {
                LatestKnownStableVersion = Version("1.0.0"),
                ErrorMessage = "Offline."
            });

        Assert.Equal("1.0.0", presentation.LatestVersionText);
        Assert.Equal("Offline.", presentation.SettingsStatusText);
    }

    [Fact]
    public void A_failed_recheck_preserves_the_known_install_action()
    {
        var presentation = UpdateStatusPresentation.Create(
            State(
                UpdateCheckStatus.Error,
                UpdateCheckTrigger.User,
                Available(),
                error: true));

        Assert.Equal("Install update…", presentation.TrayActionLabel);
        Assert.True(presentation.IsReleaseActionVisible);
        Assert.Contains("remains available", presentation.BannerMessage);
    }

    [Theory]
    [InlineData(false, "show,front,activate")]
    [InlineData(true, "show,restore,front,activate")]
    public void Window_activation_handles_visible_hidden_and_minimized_states(
        bool minimized,
        string expected)
    {
        var target = new RecordingActivationTarget { IsMinimized = minimized };
        var activation = new MainWindowActivation(target);

        activation.Activate();
        activation.Activate();

        Assert.Equal(
            string.Join(',', Enumerable.Repeat(expected, 2)),
            string.Join(',', target.Calls));
    }

    [Fact]
    public void Tray_check_activates_the_window_before_starting_a_check()
    {
        var calls = new List<string>();
        var actions = new UpdateUiActions(
            () => calls.Add("activate"),
            () => calls.Add("check"),
            _ => calls.Add("open"));

        var showedKnownUpdate = actions.InvokeTrayAction(
            State(UpdateCheckStatus.Idle, UpdateCheckTrigger.Automatic));

        Assert.False(showedKnownUpdate);
        Assert.Equal(["activate", "check"], calls);
    }

    [Fact]
    public void Tray_install_activates_without_rechecking_and_opens_the_exact_release()
    {
        var calls = new List<string>();
        Uri? opened = null;
        var update = Available();
        var state = State(
            UpdateCheckStatus.Available,
            UpdateCheckTrigger.Automatic,
            update);
        var actions = new UpdateUiActions(
            () => calls.Add("activate"),
            () => calls.Add("check"),
            uri => opened = uri);

        Assert.True(actions.InvokeTrayAction(state));
        Assert.True(actions.OpenRelease(state));

        Assert.Equal(["activate"], calls);
        Assert.Same(update.ReleasePage, opened);
        Assert.Equal(
            "https://github.com/DevSecNinja/home-assistant-win-companion/releases/tag/v2.0.0",
            opened!.AbsoluteUri);
    }

    [Fact]
    public void Recheck_action_always_requests_a_fresh_check()
    {
        var checks = 0;
        var actions = new UpdateUiActions(() => { }, () => checks++, _ => { });

        actions.Recheck();
        actions.Recheck();

        Assert.Equal(2, checks);
    }

    [Fact]
    public void Tray_commands_execute_the_bound_action()
    {
        var executions = 0;
        var command = new ActionCommand(() => executions++);

        command.Execute(null);

        Assert.Equal(1, executions);
    }

    private static UpdateCheckState State(
        UpdateCheckStatus status,
        UpdateCheckTrigger trigger,
        AvailableUpdate? update = null,
        bool error = false) =>
        new(
            status,
            trigger,
            Version("1.0.0"),
            update,
            error ? "The update check failed. Try again." : null);

    private static AvailableUpdate Available() =>
        new(
            Version("1.0.0"),
            Version("2.0.0"),
            new Uri(
                "https://github.com/DevSecNinja/home-assistant-win-companion/releases/tag/v2.0.0"));

    private static SemanticVersion Version(string value)
    {
        Assert.True(SemanticVersion.TryParse(value, out var version));
        return version!;
    }

    private sealed class RecordingActivationTarget : IMainWindowActivationTarget
    {
        public bool IsMinimized { get; init; }

        public List<string> Calls { get; } = [];

        public void Show() => Calls.Add("show");

        public void Restore() => Calls.Add("restore");

        public void BringToFront() => Calls.Add("front");

        public void Activate() => Calls.Add("activate");
    }
}
