using WindowsCompanion.Core.Updates;

namespace WindowsCompanion_App.Services;

internal enum UpdateBannerTone
{
    Informational,
    Success,
    Warning
}

internal sealed record UpdateStatusPresentation(
    string TrayActionLabel,
    string BannerTitle,
    string BannerMessage,
    UpdateBannerTone BannerTone,
    bool IsBannerOpen,
    bool IsReleaseActionVisible,
    bool IsRecheckVisible,
    bool IsRecheckEnabled,
    string InstalledVersionText,
    string LatestVersionText,
    string SettingsStatusText,
    string SettingsCheckLabel,
    bool IsSettingsInstallVisible,
    string SettingsInstallLabel = "Install update…",
    bool IsSettingsInstallEnabled = true,
    bool IsSettingsInstallActionInstall = false,
    bool IsInstallBannerActionVisible = false,
    bool IsSettingsCheckEnabled = true)
{
    internal static UpdateStatusPresentation Create(
        UpdateCheckState state,
        bool showKnownUpdate = false,
        UpdateInstallState? install = null,
        UpdateMode mode = UpdateMode.AutoInstall)
    {
        var update = state.AvailableUpdate;
        var status = showKnownUpdate && update is not null
            ? UpdateCheckStatus.Available
            : state.Status;
        var title = status switch
        {
            UpdateCheckStatus.Checking => "Checking for updates…",
            UpdateCheckStatus.Current => "You're up to date",
            UpdateCheckStatus.Available => "Update available",
            UpdateCheckStatus.Error => update is null
                ? "Couldn't check for updates"
                : "Couldn't recheck for updates",
            _ => string.Empty
        };
        var message = status switch
        {
            UpdateCheckStatus.Checking => "Looking for the latest stable release.",
            UpdateCheckStatus.Current =>
                $"Version {state.InstalledVersion} is the latest stable release.",
            UpdateCheckStatus.Available when update is not null =>
                $"Version {update.AvailableVersion} is available. "
                + $"Installed version: {update.InstalledVersion}.",
            UpdateCheckStatus.Error when update is not null =>
                $"{state.ErrorMessage} Version {update.AvailableVersion} remains available.",
            UpdateCheckStatus.Error => state.ErrorMessage ?? "The update check failed.",
            _ => string.Empty
        };
        var tone = status switch
        {
            UpdateCheckStatus.Current => UpdateBannerTone.Success,
            UpdateCheckStatus.Error => UpdateBannerTone.Warning,
            _ => UpdateBannerTone.Informational
        };
        var userVisible = state.Trigger == UpdateCheckTrigger.User
            && status != UpdateCheckStatus.Idle;
        var latestVersion = state.LatestKnownStableVersion
            ?? update?.AvailableVersion
            ?? (status == UpdateCheckStatus.Current ? state.InstalledVersion : null);

        var relevantInstall = install is not null
            && update is not null
            && install.Version.Equals(update.AvailableVersion)
            ? install
            : null;

        var (settingsStatus, installLabel, installEnabled, installAction) = DescribeInstall(
            status,
            update,
            state.ErrorMessage,
            relevantInstall,
            mode);

        return new(
            update is null ? "Check for updates…" : "Install update…",
            title,
            message,
            tone,
            status == UpdateCheckStatus.Available || userVisible,
            update is not null,
            status != UpdateCheckStatus.Idle,
            status is not (UpdateCheckStatus.Idle or UpdateCheckStatus.Checking)
                && mode != UpdateMode.Disabled,
            state.InstalledVersion.ToString(),
            latestVersion?.ToString() ?? "Not checked",
            settingsStatus,
            status == UpdateCheckStatus.Idle ? "Check for updates" : "Recheck for updates",
            update is not null,
            installLabel,
            installEnabled,
            installAction,
            relevantInstall?.Phase == UpdateInstallPhase.ReadyToInstall,
            mode != UpdateMode.Disabled);
    }

    private static (string Status, string Label, bool Enabled, bool IsInstallAction) DescribeInstall(
        UpdateCheckStatus status,
        AvailableUpdate? update,
        string? errorMessage,
        UpdateInstallState? install,
        UpdateMode mode)
    {
        var fallbackStatus = status switch
        {
            UpdateCheckStatus.Checking => "Checking for the latest stable release…",
            UpdateCheckStatus.Current => "You're up to date.",
            UpdateCheckStatus.Available when update is not null =>
                $"Version {update.AvailableVersion} is available.",
            UpdateCheckStatus.Error => errorMessage ?? "The update check failed.",
            _ => "Updates have not been checked yet."
        };

        // No download is in progress (or none will ever start, e.g. Notify-only
        // or Disabled mode): the settings action can only open the release page
        // for the user to install manually, never claim it can "Install update…".
        if (install is null || update is null)
        {
            return mode == UpdateMode.AutoInstall
                ? (fallbackStatus, "Install update…", true, false)
                : (fallbackStatus, "View release", update is not null, false);
        }

        return install.Phase switch
        {
            UpdateInstallPhase.Downloading => (
                $"Downloading version {update.AvailableVersion} ({install.DownloadProgress:P0})…",
                "Downloading…",
                false,
                false),
            UpdateInstallPhase.Verifying => (
                $"Verifying version {update.AvailableVersion}…",
                "Verifying…",
                false,
                false),
            UpdateInstallPhase.ReadyToInstall => (
                $"Version {update.AvailableVersion} is downloaded and verified.",
                "Install now",
                true,
                true),
            UpdateInstallPhase.Installing => (
                "Installing… the app will restart automatically.",
                "Installing…",
                false,
                false),
            UpdateInstallPhase.Installed => (
                $"Version {update.AvailableVersion} installed. Restarting…",
                "Installed",
                false,
                false),
            UpdateInstallPhase.Failed => (
                install.ErrorMessage ?? fallbackStatus,
                "View release",
                true,
                false),
            _ => mode == UpdateMode.AutoInstall
                ? (fallbackStatus, "Install update…", true, false)
                : (fallbackStatus, "View release", true, false)
        };
    }
}
