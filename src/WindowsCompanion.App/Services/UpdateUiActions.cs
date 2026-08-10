using WindowsCompanion.Core.Updates;

namespace WindowsCompanion_App.Services;

/// <summary>Testable behavior behind the update tray and banner actions.</summary>
internal sealed class UpdateUiActions(
    Action activateWindow,
    Action checkForUpdates,
    Action<Uri> openReleasePage)
{
    internal bool InvokeTrayAction(UpdateCheckState state)
    {
        activateWindow();
        if (state.AvailableUpdate is not null) return true;

        checkForUpdates();
        return false;
    }

    internal void Recheck() => checkForUpdates();

    internal bool OpenRelease(UpdateCheckState state)
    {
        if (state.AvailableUpdate is not { } update) return false;

        openReleasePage(update.ReleasePage);
        return true;
    }
}
