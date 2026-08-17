using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using WindowsCompanion.Core.App;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Updates;
using WindowsCompanion_App.Services;

namespace WindowsCompanion_App;

public sealed partial class MainWindow
{
    private bool _loadingStartupSetting;
    private bool _settingsActionBusy;
    private bool _loadingUpdateMode;

    private void RefreshStartupSetting()
    {
        _loadingStartupSetting = true;
        try
        {
            if (!_startup.IsSupported)
            {
                StartWithWindowsToggle.IsOn = false;
                StartWithWindowsToggle.IsEnabled = false;
                StartupStatusText.Text = "Unavailable in the isolated test profile.";
                return;
            }

            StartWithWindowsToggle.IsEnabled = true;
            var state = _startup.GetState();
            var repaired = false;
            if (state == StartupRegistrationState.NeedsRepair)
            {
                _startup.SetEnabled(true);
                state = StartupRegistrationState.Enabled;
                repaired = true;
            }

            StartWithWindowsToggle.IsOn = state == StartupRegistrationState.Enabled;
            StartupStatusText.Text = state switch
            {
                StartupRegistrationState.Enabled when repaired =>
                    "Enabled for this Windows user. The startup path was repaired.",
                StartupRegistrationState.Enabled =>
                    "Enabled for this Windows user.",
                _ => "Disabled for this Windows user."
            };
        }
        catch (Exception ex)
        {
            StartWithWindowsToggle.IsOn = false;
            StartupStatusText.Text = "Could not read Windows startup status: " + ex.Message;
        }
        finally
        {
            _loadingStartupSetting = false;
        }
    }

    private void OnStartWithWindowsToggled(object sender, RoutedEventArgs e)
    {
        if (_loadingStartupSetting || !_startup.IsSupported) return;

        try
        {
            _startup.SetEnabled(StartWithWindowsToggle.IsOn);
        }
        catch (Exception ex)
        {
            StartupStatusText.Text = "Could not update Windows startup: " + ex.Message;
        }

        RefreshStartupSetting();
    }

    private void RefreshBattery() => RefreshStatusFields();

    /// <summary>Refreshes the live fields on the status view.</summary>
    private void RefreshStatusFields()
    {
        var status = _controller.GetSystemStatus();
        BatteryText.Text = status.HasBattery
            ? $"{status.BatteryPercent}% ({status.BatteryStateString})"
            : "No battery (desktop)";

        var demo = _controller.IsDemoMode;
        ServerText.Text = demo ? DemoSession.ServerLabel : _controller.BaseUrl?.TrimEnd('/') ?? "—";
        RouteText.Text = _controller.RouteSummary;
        if (demo) StatusText.Text = DemoSession.Title;

        var last = _controller.LastSyncedAt;
        LastUpdateText.Text = demo
            ? "Never (demo mode)"
            : last is null
                ? "—"
                : $"{last.Value.ToLocalTime():HH:mm:ss} ({Ago(DateTimeOffset.UtcNow - last.Value)})";

        UpdateHealth();
        RefreshPreferencesSummary();
    }

    private void UpdateHealth()
    {
        var (healthy, summary) = _controller.Health;

        HealthText.Text = healthy ? "Healthy" : summary;
        HealthText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            healthy ? "SystemFillColorSuccessBrush" : "SystemFillColorCautionBrush"];

        // The tray tooltip is the at-a-glance view when the window is hidden.
        // The short name is used because Windows truncates the tooltip at 127
        // characters and the status summary can be long.
        TrayIcon.ToolTipText = TrayTooltipFormatter.Format(
            healthy,
            summary,
            _controller.UpdateState.AvailableUpdate?.AvailableVersion);
    }

    private void OnOpenLog(object sender, RoutedEventArgs e)
    {
        try
        {
            _controller.OpenLogFile();
            ShowSettingsActionStatus("Opened the current log file.", true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or Win32Exception or InvalidOperationException
                                   or NotSupportedException)
        {
            ShowSettingsActionStatus("Could not open the log file: " + ex.Message, false);
        }
    }

    private void OnOpenLocationSettings(object sender, RoutedEventArgs e) =>
        _controller.OpenLocationSettings();

    private static string Ago(TimeSpan span) => span.TotalSeconds switch
    {
        < 10 => "just now",
        < 60 => $"{(int)span.TotalSeconds}s ago",
        < 3600 => $"{(int)span.TotalMinutes}m ago",
        _ => $"{(int)span.TotalHours}h ago"
    };

    private void OnShowPreferences(object sender, RoutedEventArgs e)
    {
        LoadPreferences();
        ShowView(View.Preferences);
    }

    private void OnClosePreferences(object sender, RoutedEventArgs e) =>
        ShowView(View.Status);

    private void LoadPreferences()
    {
        RefreshStartupSetting();
        var catalog = _controller.Catalog;
        _loadingSensorSettings = true;
        if (catalog is not null)
            IdleMinutesBox.Value = Math.Max(1, catalog.Preferences.IdleThresholdSeconds / 60);
        _loadingSensorSettings = false;
        IdleMinutesBox.IsEnabled = catalog is not null;
        ChooseSensorsButton.IsEnabled = catalog is not null;
        SyncSensorsButton.IsEnabled = _connected;
        DisconnectButton.Content = _connected ? "Pause" : "Reconnect";
        SettingsActionInfoBar.IsOpen = false;
        _loadingUpdateMode = true;
        UpdateModeComboBox.SelectedIndex = _controller.CurrentUpdateMode switch
        {
            UpdateMode.NotifyOnly => 1,
            UpdateMode.Disabled => 2,
            _ => 0
        };
        _loadingUpdateMode = false;
        RefreshPreferencesSummary();
    }

    private void OnUpdateModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUpdateMode) return;
        if (UpdateModeComboBox.SelectedIndex < 0) return;

        var mode = UpdateModeComboBox.SelectedIndex switch
        {
            1 => UpdateMode.NotifyOnly,
            2 => UpdateMode.Disabled,
            _ => UpdateMode.AutoInstall
        };
        _controller.SetUpdateMode(mode);
    }

    private void RefreshPreferencesSummary()
    {
        var state = _controller.State;
        SettingsConnectionStatusText.Text = _controller.IsDemoMode
            ? DemoSession.Title
            : state switch
            {
                ConnectionState.Connecting => "Connecting to Home Assistant",
                ConnectionState.Connected => "Connected to Home Assistant",
                ConnectionState.Reconnecting => "Reconnecting to Home Assistant",
                ConnectionState.AuthError => "Sign-in required",
                _ => "Reporting paused"
            };
        SettingsConnectionStatusText.Foreground =
            (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                state == ConnectionState.Connected
                    ? "SystemFillColorSuccessBrush"
                    : "TextFillColorPrimaryBrush"];

        var baseUrl = _controller.BaseUrl;
        SettingsServerText.Text = _controller.IsDemoMode
            ? DemoSession.ServerLabel
            : Uri.TryCreate(baseUrl, UriKind.Absolute, out var serverUri)
                ? serverUri.Host
                : baseUrl ?? "No server configured";
        SettingsRouteText.Text = _controller.IsDemoMode
            ? "Nothing is sent to Home Assistant"
            : _controller.RouteSummary;

        var versionSummary = _controller.IsDemoMode ? null : _controller.VersionSummary;
        if (!string.IsNullOrEmpty(versionSummary))
        {
            SettingsVersionText.Text = versionSummary;
            SettingsVersionText.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
        else
        {
            SettingsVersionText.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }

        var catalog = _controller.Catalog;
        EnabledSensorCountText.Text = catalog is null
            ? "Sensor catalog unavailable"
            : $"{catalog.EnabledIds.Count} of {catalog.Definitions.Count} enabled";

        var lastSync = _controller.LastSyncedAt;
        LastSensorSyncText.Text = _controller.IsDemoMode
            ? "Sync is unavailable in demo mode"
            : lastSync is null
                ? "Not synced yet"
                : $"Last synced {Ago(DateTimeOffset.UtcNow - lastSync.Value)}";
    }

    private void SetSettingsActionBusy(bool busy)
    {
        _settingsActionBusy = busy;
        SettingsActionProgress.IsActive = busy;
        var catalogAvailable = !busy && _controller.Catalog is not null;
        SyncSensorsButton.IsEnabled = !busy && _connected;
        IdleMinutesBox.IsEnabled = catalogAvailable;
        ChooseSensorsButton.IsEnabled = catalogAvailable;
        ConnectionButton.IsEnabled = !busy;
        foreach (var control in _sensorSettingControls)
            control.IsEnabled = catalogAvailable;
        SettingsCheckUpdatesButton.IsEnabled =
            !busy && _controller.UpdateState.Status != UpdateCheckStatus.Checking;
        DisconnectButton.IsEnabled = !busy;
        RemoveServerButton.IsEnabled = !busy;
        if (busy) ShowSettingsActionStatus("Working…", true);
    }

    private void ShowSettingsActionStatus(string message, bool positive)
    {
        var messageChanged = !string.Equals(
            SettingsActionStatus.Text,
            message,
            StringComparison.Ordinal);
        SettingsActionStatus.Text = message;
        SettingsActionInfoBar.Severity = positive
            ? InfoBarSeverity.Success
            : InfoBarSeverity.Warning;
        SettingsActionInfoBar.IsOpen = true;
        if (messageChanged)
        {
            var peer = FrameworkElementAutomationPeer.FromElement(SettingsActionStatus)
                       ?? FrameworkElementAutomationPeer.CreatePeerForElement(
                           SettingsActionStatus);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }
}
