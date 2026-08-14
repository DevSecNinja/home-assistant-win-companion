using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WindowsCompanion.Core.Models;

namespace WindowsCompanion_App;

public sealed partial class MainWindow
{
    private int _connectionActionRunning;

    /// <summary>
    /// Enter in the URL box signs in. Signing in is the only action on this
    /// panel, and typing an address then pressing Enter is the reflex users
    /// bring from every browser address bar.
    /// </summary>
    private void OnUrlBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Windows.System is not imported: it would make DispatcherQueue ambiguous
        // with Microsoft.UI.Dispatching.
        if (e.Key != Windows.System.VirtualKey.Enter) return;

        // Handle it even while busy, so a second Enter cannot queue a duplicate
        // sign-in while the browser round-trip is still running.
        e.Handled = true;
        if (!SignInButton.IsEnabled) return;

        OnSignIn(SignInButton, new RoutedEventArgs());
    }

    private async void OnSignIn(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            ShowConnectError("Please enter your Home Assistant URL.");
            return;
        }

        SetSignInBusy(true);
        try
        {
            await _controller.SignInAsync(url);
            _connected = true;
            ApplyDemoChrome();
            ShowPanel(true);
            RefreshBattery();
            _statusTimer.Start();
        }
        catch (Exception ex)
        {
            ShowConnectError(ex.Message);
        }
        finally
        {
            SetSignInBusy(false);
        }
    }

    private async void OnEnterDemoMode(object sender, RoutedEventArgs e)
    {
        // A sign-in already in flight must win: entering the demo here would
        // let the OAuth round-trip finish underneath it and register with
        // Home Assistant while the demo banner still promises nothing is sent.
        if (!SignInButton.IsEnabled) return;

        DemoModeButton.IsEnabled = false;
        try
        {
            _controller.EnterDemoMode();
        }
        catch (Exception ex)
        {
            ShowConnectError(ex.Message);
            return;
        }
        finally
        {
            DemoModeButton.IsEnabled = true;
        }

        ApplyDemoChrome();
        RefreshStatusFields();
        ShowView(View.Status);
        _statusTimer.Start();

        // Seeing the sensors is the whole point of the demo, so it opens on them.
        _sensorReturnView = View.Status;
        if (await BuildSensorListAsync()) ShowView(View.Sensors);
    }

    private void OnLeaveDemoMode(object sender, RoutedEventArgs e)
    {
        _statusTimer.Stop();
        _controller.ExitDemoMode();
        ApplyDemoChrome();
        ShowView(View.Connect);
    }

    /// <summary>
    /// Shows the demo warning on every screen and hides the actions that need a
    /// Home Assistant server, so nothing in the demo looks like it talks to one.
    /// </summary>
    private void ApplyDemoChrome()
    {
        var demo = _controller.IsDemoMode;
        DemoBanner.IsOpen = demo;

        var serverActions = demo ? Visibility.Collapsed : Visibility.Visible;
        OpenHomeAssistantButton.Visibility = serverActions;
        ConnectionSettingsSection.Visibility = serverActions;
        ConnectionManagementSection.Visibility = serverActions;
        SyncSensorsButton.Visibility = serverActions;
        TrayOpenHomeAssistantItem.Visibility = serverActions;
        TrayDisconnectItem.Visibility = serverActions;
    }

    private async void OnDisconnect(object sender, RoutedEventArgs e)
    {
        // Reachable from the tray menu, where the demo has nothing to disconnect.
        if (_controller.IsDemoMode) return;

        if (Interlocked.Exchange(ref _connectionActionRunning, 1) != 0) return;

        SetSettingsActionBusy(true);
        try
        {
            if (_connected)
            {
                _statusTimer.Stop();
                await _controller.DisconnectAsync();
                _connected = false;
                DisconnectButton.Content = "Reconnect";
                SyncSensorsButton.IsEnabled = false;
                ChooseSensorsButton.IsEnabled = false;
                IdleMinutesBox.IsEnabled = false;
                StatusText.Text = "Disconnected";
                ShowSettingsActionStatus(
                    "Connection stopped. Your server and sign-in information were kept.",
                    true);
            }
            else
            {
                if (!await _controller.ReconnectAsync())
                {
                    ReconcileConnectionControlsAfterFailure();
                    ShowSettingsActionStatus(
                        "Could not reconnect because the saved server or sign-in is unavailable.",
                        false);
                    return;
                }
                _connected = true;
                DisconnectButton.Content = "Pause";
                SyncSensorsButton.IsEnabled = true;
                ChooseSensorsButton.IsEnabled = true;
                IdleMinutesBox.IsEnabled = true;
                _statusTimer.Start();
                ShowSettingsActionStatus("Reconnected to Home Assistant.", true);
            }
            RefreshPreferencesSummary();
        }
        catch (OperationCanceledException) when (_exiting)
        {
            // Application shutdown superseded this user action.
        }
        catch (Exception ex)
        {
            ReconcileConnectionControlsAfterFailure();
            ShowSettingsActionStatus("Could not change the connection: " + ex.Message, false);
        }
        finally
        {
            SetSettingsActionBusy(false);
            Interlocked.Exchange(ref _connectionActionRunning, 0);
        }
    }

    private async void OnRemoveServer(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Remove server?",
            Content = "This signs out of Home Assistant, revokes this PC's access token and "
                      + "deletes the saved server. You will need to sign in again.",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        PrepareDialog(dialog);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var homeAssistantUrl = _controller.BaseUrl;
        _statusTimer.Stop();
        SetSettingsActionBusy(true);
        try
        {
            await _controller.RemoveServerAsync();
        }
        catch (Exception ex)
        {
            ReconcileConnectionControlsAfterFailure();
            ShowSettingsActionStatus("Could not remove the server: " + ex.Message, false);
            return;
        }
        finally
        {
            SetSettingsActionBusy(false);
        }
        _connected = false;
        DisconnectButton.Content = "Pause";
        SyncSensorsButton.IsEnabled = false;
        ChooseSensorsButton.IsEnabled = false;
        IdleMinutesBox.IsEnabled = false;
        // Nothing is connected any more, so the demo becomes available again.
        DemoModeButton.IsEnabled = true;
        ShowView(View.Connect);

#if DEBUG
        if (App.TestLaunchOptions is not null) return;
#endif
        var removed = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Server removed from this PC",
            Content = "The saved sign-in and local server settings were removed. "
                      + "Home Assistant keeps the Mobile App device and its entities because "
                      + "its app API does not provide a delete operation. To remove them too, "
                      + "open Home Assistant and delete this device under Settings → Devices "
                      + "& services → Mobile App.",
            PrimaryButtonText = string.IsNullOrWhiteSpace(homeAssistantUrl)
                ? string.Empty
                : "Open Home Assistant",
            CloseButtonText = "Done",
            DefaultButton = ContentDialogButton.Close
        };
        PrepareDialog(removed);

        if (await removed.ShowAsync() == ContentDialogResult.Primary
            && !string.IsNullOrWhiteSpace(homeAssistantUrl))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = homeAssistantUrl,
                UseShellExecute = true
            });
        }
    }

    private async void OnForcePush(object sender, RoutedEventArgs e)
    {
        SyncSensorsButton.IsEnabled = false;
        SetSettingsActionBusy(true);
        try
        {
            await _controller.ForcePushAsync();
            RefreshStatusFields();
            ShowSettingsActionStatus("Enabled sensor states synced to Home Assistant.", true);
        }
        catch (Exception ex)
        {
            ShowSettingsActionStatus("Could not sync sensors: " + ex.Message, false);
        }
        finally
        {
            SetSettingsActionBusy(false);
            SyncSensorsButton.IsEnabled = _connected;
        }
    }

    private void OnOpenHomeAssistant(object sender, RoutedEventArgs e) =>
        _controller.OpenHomeAssistant();

    private void ReconcileConnectionControlsAfterFailure()
    {
        _connected = _controller.State
            is not (ConnectionState.Disconnected or ConnectionState.AuthError);
        DisconnectButton.Content = _connected ? "Pause" : "Reconnect";
        var catalogAvailable = _controller.Catalog is not null;
        SyncSensorsButton.IsEnabled = _connected;
        ChooseSensorsButton.IsEnabled = catalogAvailable;
        IdleMinutesBox.IsEnabled = catalogAvailable;
        if (_connected) _statusTimer.Start();
        else
        {
            _statusTimer.Stop();
            StatusText.Text = "Disconnected";
        }
    }

    private void SetSignInBusy(bool busy)
    {
        SignInButton.IsEnabled = !busy;
        UrlBox.IsEnabled = !busy;
        // A demo started while a sign-in is in flight would let the OAuth
        // round-trip finish underneath it and register with Home Assistant
        // while the demo banner still promises nothing is sent.
        DemoModeButton.IsEnabled = !busy;
        SignInProgress.IsActive = busy;
        if (busy) ConnectError.Visibility = Visibility.Collapsed;
    }

    private void ShowConnectError(string message)
    {
        ConnectError.Text = message;
        ConnectError.Visibility = Visibility.Visible;
    }
}
