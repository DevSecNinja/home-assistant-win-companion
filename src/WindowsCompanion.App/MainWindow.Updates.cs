using System.ComponentModel;
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

    private void OnUpdateStateChanged(UpdateCheckState state) =>
        _dispatcher.TryEnqueue(() => ApplyUpdateState(state));

    private void ApplyUpdateState(UpdateCheckState state, bool showKnownUpdate = false)
    {
        if (_exiting || state.Revision < _controller.UpdateState.Revision) return;

        var presentation = UpdateStatusPresentation.Create(state, showKnownUpdate);
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
            state.Status != UpdateCheckStatus.Checking;
        SettingsInstallUpdateButton.Visibility = presentation.IsSettingsInstallVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
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

    private void OnRecheckUpdates(object sender, RoutedEventArgs e) =>
        _updateActions.Recheck();

    private void HandleUpdateTrayAction()
    {
        UpdateBanner.Visibility = Visibility.Visible;
        var state = _controller.UpdateState;
        if (_updateActions.InvokeTrayAction(state))
            ApplyUpdateState(state, showKnownUpdate: true);
    }

    /// <summary>
    /// Swaps the tray icon between the normal and "update available" glyph.
    /// Best-effort: a bad or missing packaged resource must not crash the app,
    /// so any failure here just leaves the previous icon in place.
    /// </summary>
    private void ApplyTrayIcon(bool isDefault)
    {
        try
        {
            TrayIcon.IconSource = new BitmapImage(new Uri(
                isDefault
                    ? "ms-appx:///Assets/AppIcon.ico"
                    : "ms-appx:///Assets/UpdateIcon.ico"));
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
