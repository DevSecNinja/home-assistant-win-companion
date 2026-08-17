using System.ComponentModel;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using WindowsCompanion.Core.Updates;
using WindowsCompanion_App.Services;

namespace WindowsCompanion_App;

public sealed partial class MainWindow
{
    private readonly UpdateUiActions _updateActions;
    private bool _settingsInstallActionIsInstall;

    /// <summary>
    /// True while the one-time silent-install result banner is showing. An
    /// automatic update check that completes immediately after startup (before
    /// the user has seen or acted on that banner) must not silently overwrite
    /// it via <see cref="ApplyUpdateState"/> - the banner is dismissed only by
    /// the user closing it or acting on one of its buttons.
    /// </summary>
    private bool _showingLastInstallResult;

    private void ShowLastInstallResultIfAny()
    {
        var result = _controller.LastInstallResult;
        if (result is null) return;

        UpdateBannerTitleText.Text = result.Success
            ? "Update installed"
            : "The update could not be installed";
        UpdateBannerMessage.Text = result.Success
            ? $"Windows Companion was updated to version {result.Version}."
            : "The silent install failed. You can open the release page and install it manually.";
        UpdateBanner.Severity = result.Success
            ? InfoBarSeverity.Success
            : InfoBarSeverity.Warning;
        ViewReleaseButton.Visibility = result.Success ? Visibility.Collapsed : Visibility.Visible;
        InstallNowButton.Visibility = Visibility.Collapsed;
        RecheckUpdatesButton.Visibility = Visibility.Visible;
        RecheckUpdatesButton.IsEnabled = true;
        _showingLastInstallResult = true;
        UpdateBanner.IsOpen = true;
    }

    private void OnUpdateStateChanged(UpdateCheckState state) =>
        _dispatcher.TryEnqueue(() => ApplyUpdateState(state));

    private void OnInstallStateChanged(UpdateInstallState state) =>
        _dispatcher.TryEnqueue(() => ApplyUpdateState(_controller.UpdateState));

    private void ApplyUpdateState(UpdateCheckState state, bool showKnownUpdate = false)
    {
        if (_exiting || state.Revision < _controller.UpdateState.Revision) return;
        if (_showingLastInstallResult) return;

        var presentation = UpdateStatusPresentation.Create(
            state,
            showKnownUpdate,
            _controller.InstallState,
            _controller.CurrentUpdateMode);
        ApplyTrayIcon(state.AvailableUpdate is null);
        TrayUpdateItem.Text = presentation.TrayActionLabel;
        UpdateBannerTitleText.Text = presentation.BannerTitle;
        var messageChanged = !string.Equals(
            UpdateBannerMessage.Text,
            presentation.BannerMessage,
            StringComparison.Ordinal);
        UpdateBannerMessage.Text = presentation.BannerMessage;
        UpdateBanner.Severity = presentation.BannerTone switch
        {
            UpdateBannerTone.Success => InfoBarSeverity.Success,
            UpdateBannerTone.Warning => InfoBarSeverity.Warning,
            _ => InfoBarSeverity.Informational
        };
        UpdateBanner.IsOpen = presentation.IsBannerOpen;
        ViewReleaseButton.Visibility = presentation.IsReleaseActionVisible
            && !presentation.IsInstallBannerActionVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        InstallNowButton.Visibility = presentation.IsInstallBannerActionVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        RecheckUpdatesButton.Visibility = presentation.IsRecheckVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        RecheckUpdatesButton.IsEnabled = presentation.IsRecheckEnabled;
        InstalledVersionText.Text = presentation.InstalledVersionText;
        LatestVersionText.Text = presentation.LatestVersionText;
        var settingsUpdateChanged = !string.Equals(
            SettingsUpdateStatusText.Text,
            presentation.SettingsStatusText,
            StringComparison.Ordinal);
        SettingsUpdateStatusText.Text = presentation.SettingsStatusText;
        SettingsUpdateProgress.IsActive = state.Status == UpdateCheckStatus.Checking;
        SettingsUpdateProgress.Visibility = state.Status == UpdateCheckStatus.Checking
            ? Visibility.Visible
            : Visibility.Collapsed;
        SettingsCheckUpdatesButton.Content = presentation.SettingsCheckLabel;
        SettingsCheckUpdatesButton.IsEnabled =
            state.Status != UpdateCheckStatus.Checking && presentation.IsSettingsCheckEnabled;
        SettingsInstallUpdateButton.Visibility = presentation.IsSettingsInstallVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        SettingsInstallUpdateButton.Content = presentation.SettingsInstallLabel;
        SettingsInstallUpdateButton.IsEnabled = presentation.IsSettingsInstallEnabled;
        _settingsInstallActionIsInstall = presentation.IsSettingsInstallActionInstall;
        if (presentation.IsBannerOpen && messageChanged)
        {
            var peer = FrameworkElementAutomationPeer.FromElement(UpdateBannerMessage)
                       ?? FrameworkElementAutomationPeer.CreatePeerForElement(
                           UpdateBannerMessage);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        if (settingsUpdateChanged)
        {
            var peer = FrameworkElementAutomationPeer.FromElement(SettingsUpdateStatusText)
                       ?? FrameworkElementAutomationPeer.CreatePeerForElement(
                           SettingsUpdateStatusText);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        UpdateHealth();
    }

    private void OnViewRelease(object sender, RoutedEventArgs e)
    {
        _showingLastInstallResult = false;
        try
        {
            _updateActions.OpenRelease(_controller.UpdateState);
        }
        catch (Win32Exception)
        {
            ShowReleaseLaunchFailure();
        }
        catch (InvalidOperationException)
        {
            ShowReleaseLaunchFailure();
        }
        catch (NotSupportedException)
        {
            ShowReleaseLaunchFailure();
        }
    }

    private void OnInstallNow(object sender, RoutedEventArgs e)
    {
        _showingLastInstallResult = false;
        _ = RunInstallAsync();
    }

    private void OnSettingsInstallUpdate(object sender, RoutedEventArgs e)
    {
        if (_settingsInstallActionIsInstall)
        {
            _showingLastInstallResult = false;
            _ = RunInstallAsync();
            return;
        }

        OnViewRelease(sender, e);
    }

    /// <summary>
    /// Runs the silent install and, once the detached helper has been handed
    /// off successfully, requests our own graceful shutdown so the helper's
    /// wait for this process to exit does not stall indefinitely on the user
    /// closing the app manually.
    /// </summary>
    private async Task RunInstallAsync()
    {
        var handoffSucceeded = await _controller.InstallUpdateAsync();
        if (handoffSucceeded)
            ((App)Application.Current).RequestShutdown(AppShutdownReason.UpdateInstall);
    }

    private void OnRecheckUpdates(object sender, RoutedEventArgs e)
    {
        _showingLastInstallResult = false;
        _updateActions.Recheck();
    }

    private void HandleUpdateTrayAction()
    {
        UpdateBanner.Visibility = Visibility.Visible;
        var state = _controller.UpdateState;
        if (_updateActions.InvokeTrayAction(state))
            ApplyUpdateState(state, showKnownUpdate: true);
    }

    /// <summary>
    /// Swaps the tray icon between the normal and "update available" glyph.
    /// </summary>
    /// <remarks>
    /// This app ships unpackaged (see the "CopyToOutputDirectory" comment on the
    /// Assets items in WindowsCompanion.App.csproj), so an "ms-appx:///" URI has
    /// no package identity to resolve against and fails. Build an absolute
    /// file:// URI against the app's own base directory instead, matching how
    /// AppWindow.SetIcon and the XAML-declared icons resolve "Assets/*.ico".
    /// Still wrapped defensively: a missing/corrupt icon file must not crash the
    /// app, so a failure here just logs and leaves the previous icon in place.
    /// </remarks>
    private void ApplyTrayIcon(bool isDefault)
    {
        try
        {
            var fileName = isDefault ? "AppIcon.ico" : "UpdateIcon.ico";
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
            TrayIcon.IconSource = new BitmapImage(new Uri(path));
        }
        catch (Exception ex)
        {
            FileLoggerProvider.Write(
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} ERR MainWindow: "
                + $"Could not load the tray icon.{Environment.NewLine}{ex}");
        }
    }

    private static void OpenReleasePage(Uri releasePage)
    {
        if (System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = releasePage.AbsoluteUri,
            UseShellExecute = true
        }) is null)
        {
            throw new InvalidOperationException("Windows did not start a browser.");
        }
    }

    private void ShowReleaseLaunchFailure()
    {
        const string message =
            "Windows couldn't open the browser. You can retry or recheck for updates.";
        if (PreferencesPanel.Visibility == Visibility.Visible)
        {
            ShowSettingsActionStatus(message, false);
            return;
        }

        UpdateBanner.Visibility = Visibility.Visible;
        UpdateBannerTitleText.Text = "Couldn't open the release page";
        var messageChanged = !string.Equals(
            UpdateBannerMessage.Text,
            message,
            StringComparison.Ordinal);
        UpdateBannerMessage.Text = message;
        UpdateBanner.Severity = InfoBarSeverity.Warning;
        UpdateBanner.IsOpen = true;
        ViewReleaseButton.Visibility = Visibility.Visible;
        RecheckUpdatesButton.Visibility = Visibility.Visible;
        RecheckUpdatesButton.IsEnabled = true;
        if (messageChanged)
        {
            var peer = FrameworkElementAutomationPeer.FromElement(UpdateBannerMessage)
                       ?? FrameworkElementAutomationPeer.CreatePeerForElement(
                           UpdateBannerMessage);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }
}
