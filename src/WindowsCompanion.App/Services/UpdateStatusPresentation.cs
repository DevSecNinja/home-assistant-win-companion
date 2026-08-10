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
    bool IsRecheckEnabled)
{
    internal static UpdateStatusPresentation Create(
        UpdateCheckState state,
        bool showKnownUpdate = false)
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

        return new(
            update is null ? "Check for updates…" : "Install update…",
            title,
            message,
            tone,
            status == UpdateCheckStatus.Available || userVisible,
            update is not null,
            status != UpdateCheckStatus.Idle,
            status is not (UpdateCheckStatus.Idle or UpdateCheckStatus.Checking));
    }
}
