using WindowsCompanion.Core.App;
using WindowsCompanion.Core.Lifecycle;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;
using WindowsCompanion_App.Services;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace WindowsCompanion_App;

/// <summary>
/// The single application window. Shows a Connect view until a Home Assistant
/// session is established, then a lean Status view. The app is tray-resident:
/// closing the window hides it to the notification area rather than exiting.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int InitialWindowWidth = 720;
    private const int InitialWindowHeight = 820;
    private const int MinimumWindowWidth = 520;
    private const int MinimumWindowHeight = 600;

    private readonly AppController _controller;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _statusTimer;
    private readonly WindowsStartupRegistration _startup = new();
    private readonly bool _startHidden;
    private bool _exiting;
    private bool _connected;
    private int _sensorListBuildVersion;
    private bool _suppressSensorToggle;
    private List<string> _trustedSsids = [];
    private List<string> _trustedBssids = [];
    private bool _suppressSeparateUrlsToggle;
    private bool _suppressBssidToggle;
    private bool _loadingSensorSettings;
    private bool _loadingStartupSetting;

    public MainWindow(bool startHidden = false)
    {
        InitializeComponent();
        _startHidden = startHidden;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        var dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        AppWindow.Resize(new SizeInt32(
            ScaleForDpi(InitialWindowWidth, dpi),
            ScaleForDpi(InitialWindowHeight, dpi)));
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = ScaleForDpi(MinimumWindowWidth, dpi);
            presenter.PreferredMinimumHeight = ScaleForDpi(MinimumWindowHeight, dpi);
        }

        _controller = App.Controller;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _controller.StateChanged += OnStateChanged;
        _controller.RouteChanged += OnRouteChanged;

        // Kept in Core so the demo warning reads the same wherever it is shown.
        DemoBanner.Title = DemoSession.Title;
        DemoBanner.Message = DemoSession.Message;

        _statusTimer = _dispatcher.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromSeconds(5);
        _statusTimer.Tick += (_, _) => RefreshBattery();

        AppWindow.Closing += OnWindowClosing;
        Activated += OnFirstActivated;
    }

    private static int ScaleForDpi(int logicalPixels, uint dpi) =>
        (int)Math.Round(logicalPixels * Math.Max(96u, dpi) / 96d);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    private async void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;
        if (_startHidden) AppWindow.Hide();
        try
        {
            var resumed = await _controller.TryResumeAsync();
            ShowPanel(resumed);
            if (!resumed && _startHidden) Show();
            RefreshStartupSetting();
            if (resumed)
            {
                RefreshBattery();
                _statusTimer.Start();
            }
        }
        catch
        {
            ShowPanel(false);
            if (_startHidden) Show();
        }
    }

    private void OnRouteChanged() =>
        _dispatcher.TryEnqueue(() =>
        {
            RouteText.Text = _controller.RouteSummary;
            ServerText.Text = _controller.IsDemoMode
                ? DemoSession.ServerLabel
                : _controller.BaseUrl ?? "—";
            UpdateHealth();
        });

    private void OnStateChanged(ConnectionState state) =>
        _dispatcher.TryEnqueue(() =>
        {
            StatusText.Text = state switch
            {
                ConnectionState.Connecting => "Connecting…",
                ConnectionState.Connected => "Connected",
                ConnectionState.Reconnecting => "Reconnecting…",
                ConnectionState.AuthError => "Sign-in required",
                _ => "Disconnected"
            };

            if (state == ConnectionState.AuthError)
            {
                ShowPanel(false);
                Show();
            }

            UpdateHealth();
        });

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
        if (await BuildSensorListAsync()) ShowView(View.Settings);
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
        ConnectionButton.Visibility = serverActions;
        UpdateNowButton.Visibility = serverActions;
        DisconnectButton.Visibility = serverActions;
        RemoveServerButton.Visibility = serverActions;
    }

    private async void OnDisconnect(object sender, RoutedEventArgs e)
    {
        // Reachable from the tray menu, where the demo has nothing to disconnect.
        if (_controller.IsDemoMode) return;

        if (_connected)
        {
            _statusTimer.Stop();
            await _controller.DisconnectAsync();
            _connected = false;
            DisconnectButton.Content = "Reconnect";
            UpdateNowButton.IsEnabled = false;
            StatusText.Text = "Disconnected";
        }
        else
        {
            DisconnectButton.IsEnabled = false;
            try
            {
                await _controller.ReconnectAsync();
                _connected = true;
                DisconnectButton.Content = "Disconnect";
                UpdateNowButton.IsEnabled = true;
                _statusTimer.Start();
            }
            finally
            {
                DisconnectButton.IsEnabled = true;
            }
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

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var homeAssistantUrl = _controller.BaseUrl;
        _statusTimer.Stop();
        try
        {
            await _controller.RemoveServerAsync();
        }
        catch
        {
            // Local state is cleared regardless.
        }
        _connected = false;
        DisconnectButton.Content = "Disconnect";
        UpdateNowButton.IsEnabled = true;
        ShowView(View.Connect);

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
        UpdateNowButton.IsEnabled = false;
        try
        {
            await _controller.ForcePushAsync();
            RefreshStatusFields();
        }
        finally
        {
            UpdateNowButton.IsEnabled = true;
        }
    }

    private void OnOpenHomeAssistant(object sender, RoutedEventArgs e) => _controller.OpenHomeAssistant();

    private void OnShowConnection(object sender, RoutedEventArgs e)
    {
        LoadConnectionSettings();
        ShowView(View.Connection);
    }

    private void OnCloseConnection(object sender, RoutedEventArgs e)
    {
        RefreshStatusFields();
        ShowView(View.Status);
    }

    /// <summary>Fills the connection view from the saved settings.</summary>
    private void LoadConnectionSettings()
    {
        var settings = _controller.ConnectionSettings;
        SingleUrlBox.Text = settings.PrimaryUrl ?? _controller.BaseUrl ?? string.Empty;
        _suppressSeparateUrlsToggle = true;
        UseSeparateUrlsBox.IsChecked = settings.UseSeparateUrls;
        _suppressSeparateUrlsToggle = false;
        InternalUrlBox.Text = settings.InternalUrl ?? string.Empty;
        ExternalUrlBox.Text = settings.ExternalUrl ?? string.Empty;
        ConnectionModeBox.SelectedIndex = (int)settings.Mode;
        _trustedSsids = [.. settings.TrustedNetworks.Ssids];
        _trustedBssids = [.. settings.TrustedNetworks.Bssids];

        _suppressBssidToggle = true;
        RequireBssidBox.IsChecked = settings.TrustedNetworks.RequireBssidMatch;
        _suppressBssidToggle = false;

        TrustWiredBox.IsChecked = settings.TrustedNetworks.TrustWiredNetworks;
        ProbeUnknownBox.IsChecked = settings.TrustedNetworks.ProbeInternalOnUnknownNetworks;

        AcknowledgeUnreachableBox.IsChecked = false;
        AcknowledgeUnreachableBox.Visibility = Visibility.Collapsed;
        ConnectionResultText.Visibility = Visibility.Collapsed;
        SuggestionText.Text = string.Empty;

        UpdateSeparateUrlsVisibility();
        RefreshTrustedNetworkList();
    }

    private void OnUseSeparateUrlsChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressSeparateUrlsToggle) return;
        UpdateSeparateUrlsVisibility();
        ConnectionResultText.Visibility = Visibility.Collapsed;
        AcknowledgeUnreachableBox.Visibility = Visibility.Collapsed;
    }

    private void UpdateSeparateUrlsVisibility()
    {
        var separate = UseSeparateUrlsBox.IsChecked == true;
        SingleUrlBox.Visibility = separate ? Visibility.Collapsed : Visibility.Visible;
        SeparateUrlsPanel.Visibility = separate ? Visibility.Visible : Visibility.Collapsed;
        TestRoutesButton.Content = separate ? "Test both URLs" : "Test URL";
    }

    private void RefreshTrustedNetworkList()
    {
        var network = _controller.CurrentNetwork;
        CurrentNetworkText.Text = network switch
        {
            { Kind: NetworkKind.Wireless, Ssid: { Length: > 0 } ssid } => $"Now on Wi-Fi “{ssid}”",
            { Kind: NetworkKind.Wireless } => "Now on Wi-Fi (Windows will not reveal the name)",
            { Kind: NetworkKind.Wired } => "Now on a wired network",
            { Kind: NetworkKind.Offline } => "Not connected to a network",
            _ => "Network type unknown"
        };
        TrustNetworkButton.IsEnabled = network is { Kind: NetworkKind.Wireless, Ssid: { Length: > 0 } }
                                       && !_trustedSsids.Contains(network.Ssid, StringComparer.Ordinal);

        TrustedNetworkList.Children.Clear();
        if (_trustedSsids.Count == 0)
        {
            TrustedNetworkList.Children.Add(new TextBlock
            {
                Text = "No trusted Wi-Fi networks yet.",
                FontSize = 12,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
            return;
        }

        foreach (var ssid in _trustedSsids.ToList())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock { Text = ssid, VerticalAlignment = VerticalAlignment.Center });
            var remove = new Button { Content = "Remove", Tag = ssid };
            remove.Click += OnRemoveTrustedNetwork;
            row.Children.Add(remove);
            TrustedNetworkList.Children.Add(row);
        }
    }

    private void OnTrustCurrentNetwork(object sender, RoutedEventArgs e)
    {
        var network = _controller.CurrentNetwork;
        if (network.Ssid is not { Length: > 0 } ssid) return;

        if (!_trustedSsids.Contains(ssid, StringComparer.Ordinal)) _trustedSsids.Add(ssid);

        // A BSSID is precise location data, so it is only ever recorded when the
        // user has asked for access-point matching.
        if (RequireBssidBox.IsChecked == true
            && network.Bssid is { Length: > 0 } bssid
            && !_trustedBssids.Contains(bssid, StringComparer.OrdinalIgnoreCase))
        {
            _trustedBssids.Add(bssid);
        }

        RefreshTrustedNetworkList();
    }

    /// <summary>Turning access-point matching off also discards what it recorded.</summary>
    private void OnRequireBssidChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressBssidToggle) return;

        if (RequireBssidBox.IsChecked == true)
        {
            OnTrustCurrentNetwork(sender, e);
            return;
        }

        _trustedBssids.Clear();
        RefreshTrustedNetworkList();
    }

    private void OnRemoveTrustedNetwork(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string ssid }) return;
        _trustedSsids.RemoveAll(s => string.Equals(s, ssid, StringComparison.Ordinal));
        // Access-point addresses are not tied to a single network name, so the only
        // safe moment to drop them is when no trusted network is left.
        if (_trustedSsids.Count == 0) _trustedBssids.Clear();
        RefreshTrustedNetworkList();
    }

    private ConnectionSettingsDraft BuildDraft() => new()
    {
        PrimaryUrl = SingleUrlBox.Text?.Trim(),
        UseSeparateUrls = UseSeparateUrlsBox.IsChecked == true,
        InternalUrl = UseSeparateUrlsBox.IsChecked == true ? InternalUrlBox.Text?.Trim() : null,
        ExternalUrl = UseSeparateUrlsBox.IsChecked == true ? ExternalUrlBox.Text?.Trim() : null,
        Mode = (ConnectionMode)Math.Max(0, ConnectionModeBox.SelectedIndex),
        AcknowledgeUnreachable = AcknowledgeUnreachableBox.IsChecked == true,
        TrustedNetworks = new TrustedNetworkSettings
        {
            Ssids = [.. _trustedSsids],
            Bssids = [.. _trustedBssids],
            RequireBssidMatch = RequireBssidBox.IsChecked == true,
            TrustWiredNetworks = TrustWiredBox.IsChecked == true,
            ProbeInternalOnUnknownNetworks = ProbeUnknownBox.IsChecked == true
        }
    };

    private async void OnTestRoutes(object sender, RoutedEventArgs e)
    {
        SetConnectionBusy(true);
        try
        {
            ShowValidationReport(await _controller.TestConnectionSettingsAsync(BuildDraft()));
        }
        catch (Exception ex)
        {
            ShowConnectionResult(ex.Message, false);
        }
        finally
        {
            SetConnectionBusy(false);
        }
    }

    private async void OnSaveRoutes(object sender, RoutedEventArgs e)
    {
        SetConnectionBusy(true);
        try
        {
            var report = await _controller.SaveConnectionSettingsAsync(BuildDraft());
            ShowValidationReport(report);
            if (report.CanSave)
            {
                AcknowledgeUnreachableBox.Visibility = Visibility.Collapsed;
                RefreshStatusFields();
                return;
            }

            if (report.RequiresSignIn) await OfferReplaceServerAsync();
        }
        catch (Exception ex)
        {
            ShowConnectionResult(ex.Message, false);
        }
        finally
        {
            SetConnectionBusy(false);
        }
    }

    /// <summary>
    /// The addresses reach a different instance, so keeping the session is not an
    /// option. Replacing is destructive, so it is always an explicit choice.
    /// </summary>
    private async Task OfferReplaceServerAsync()
    {
        var url = UseSeparateUrlsBox.IsChecked != true
            ? SingleUrlBox.Text?.Trim()
            : ExternalUrlBox.Text?.Trim() is { Length: > 0 } external
            ? external
            : InternalUrlBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(url)) return;

        var replace = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Sign in to a different server?",
            Content = "The saved credentials are not valid at this address. Replacing the "
                      + "server revokes the current session and creates a new Mobile App "
                      + "device after browser sign-in.",
            PrimaryButtonText = "Replace and sign in",
            CloseButtonText = "Keep current server",
            DefaultButton = ContentDialogButton.Close
        };

        if (await replace.ShowAsync() != ContentDialogResult.Primary) return;

        await _controller.RemoveServerAsync();
        try
        {
            await _controller.SignInAsync(url);
            _connected = true;
            DisconnectButton.Content = "Disconnect";
            UpdateNowButton.IsEnabled = true;
            _statusTimer.Start();
            ShowPanel(true);
            RefreshStatusFields();
        }
        catch (Exception ex)
        {
            _connected = false;
            ShowView(View.Connect);
            ShowConnectError(ex.Message);
        }
    }

    private void ShowValidationReport(RouteValidationReport report)
    {
        var lines = new List<string> { report.Summary };
        foreach (var entry in report.Entries)
        {
            var label = UseSeparateUrlsBox.IsChecked != true
                ? "Address"
                : entry.Route == RouteKind.Internal ? "Internal" : "External";
            lines.Add($"{label}: {entry.Describe()}");
        }

        AcknowledgeUnreachableBox.Visibility = report.RequiresAcknowledgement
            ? Visibility.Visible
            : AcknowledgeUnreachableBox.Visibility;

        ShowConnectionResult(string.Join(Environment.NewLine, lines), report.CanSave);
    }

    private void ShowConnectionResult(string message, bool positive)
    {
        ConnectionResultText.Text = message;
        ConnectionResultText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            positive ? "SystemFillColorSuccessBrush" : "SystemFillColorCautionBrush"];
        ConnectionResultText.Visibility = Visibility.Visible;
    }

    private void SetConnectionBusy(bool busy)
    {
        ConnectionProgress.IsActive = busy;
        TestRoutesButton.IsEnabled = !busy;
        SaveRoutesButton.IsEnabled = !busy;
    }

    private async void OnSuggestUrls(object sender, RoutedEventArgs e)
    {
        var (internalUrl, externalUrl) = await _controller.SuggestedUrlsAsync();
        var found = new List<string>();

        if (!string.IsNullOrWhiteSpace(internalUrl) && string.IsNullOrWhiteSpace(InternalUrlBox.Text))
        {
            InternalUrlBox.Text = internalUrl;
            found.Add("internal");
        }

        if (!string.IsNullOrWhiteSpace(externalUrl) && string.IsNullOrWhiteSpace(ExternalUrlBox.Text))
        {
            ExternalUrlBox.Text = externalUrl;
            found.Add("external");
        }

        SuggestionText.Text = found.Count == 0
            ? "Home Assistant did not offer an address to fill in."
            : $"Filled in the {string.Join(" and ", found)} address; check it before saving.";
    }

    private void OnShowWindow(object sender, RoutedEventArgs e) => Show();

    private void OnExit(object sender, RoutedEventArgs e)
    {
        if (_exiting) return;
        _exiting = true;

        // Let the tray flyout finish dispatching its click before disposing the
        // icon and its WinUI objects. Tearing them down inside their own callback
        // can raise a CoreMessaging stowed exception.
        if (!_dispatcher.TryEnqueue(async () => await CompleteExitAsync()))
            _exiting = false;
    }

    internal async Task RequestExitAsync()
    {
        if (_exiting) return;

        _exiting = true;
        await CompleteExitAsync();
    }

    private async Task CompleteExitAsync()
    {
        TrayIcon.Dispose();
        await ((App)Application.Current).ShutdownAsync();
    }

    private void OnWindowClosing(Microsoft.UI.Windowing.AppWindow sender,
        Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_exiting) return;
        args.Cancel = true;
        AppWindow.Hide();
    }

    private void Show()
    {
        RefreshStartupSetting();
        AppWindow.Show();
        AppWindow.MoveInZOrderAtTop();
    }

    private void RefreshStartupSetting()
    {
        _loadingStartupSetting = true;
        try
        {
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
        if (_loadingStartupSetting) return;

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
        ServerText.Text = demo ? DemoSession.ServerLabel : _controller.BaseUrl ?? "—";
        RouteText.Text = _controller.RouteSummary;
        if (demo) StatusText.Text = DemoSession.Title;

        var last = _controller.LastSyncedAt;
        LastUpdateText.Text = demo
            ? "Never (demo mode)"
            : last is null
                ? "—"
                : $"{last.Value.ToLocalTime():HH:mm:ss} ({Ago(DateTimeOffset.UtcNow - last.Value)})";

        UpdateHealth();
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
        TrayIcon.ToolTipText = healthy
            ? $"{Branding.ShortName} — Healthy"
            : $"{Branding.ShortName} — {summary}";
    }

    private void OnOpenLog(object sender, RoutedEventArgs e) => _controller.OpenLogFile();

    private void OnOpenLocationSettings(object sender, RoutedEventArgs e) =>
        _controller.OpenLocationSettings();

    private static string Ago(TimeSpan span) => span.TotalSeconds switch
    {
        < 10 => "just now",
        < 60 => $"{(int)span.TotalSeconds}s ago",
        < 3600 => $"{(int)span.TotalMinutes}m ago",
        _ => $"{(int)span.TotalHours}h ago"
    };

    private void ShowPanel(bool connected)
    {
        ShowView(connected ? View.Status : View.Connect);
    }

    private enum View { Connect, Status, Settings, Connection }

    private void ShowView(View view)
    {
        ConnectPanel.Visibility = view == View.Connect ? Visibility.Visible : Visibility.Collapsed;
        StatusPanel.Visibility = view == View.Status ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = view == View.Settings ? Visibility.Visible : Visibility.Collapsed;
        ConnectionPanel.Visibility = view == View.Connection ? Visibility.Visible : Visibility.Collapsed;

        if (view == View.Connect)
        {
            ConnectError.Visibility = Visibility.Collapsed;
            _statusTimer.Stop();
        }
    }

    private async void OnShowSettings(object sender, RoutedEventArgs e)
    {
        if (await BuildSensorListAsync())
            ShowView(View.Settings);
    }

    private void OnCloseSettings(object sender, RoutedEventArgs e) => ShowView(View.Status);

    /// <summary>
    /// Renders one toggle per catalog sensor. Built in code rather than bound so the
    /// list always reflects whatever sources the controller actually wired up.
    /// </summary>
    private async Task<bool> BuildSensorListAsync()
    {
        var catalog = _controller.Catalog;
        if (catalog is null) return false;

        var buildVersion = ++_sensorListBuildVersion;
        var previews = await catalog.PreviewAsync();
        if (buildVersion != _sensorListBuildVersion
            || !ReferenceEquals(catalog, _controller.Catalog)
            || (!_controller.IsDemoMode
                && _controller.State is ConnectionState.Disconnected or ConnectionState.AuthError))
        {
            return false;
        }

        SensorList.Children.Clear();
        _loadingSensorSettings = true;
        IdleMinutesBox.Value = Math.Max(1, catalog.Preferences.IdleThresholdSeconds / 60);
        _loadingSensorSettings = false;
        foreach (var definition in catalog.Definitions)
        {
            var toggle = new ToggleSwitch
            {
                IsOn = catalog.IsEnabled(definition.UniqueId),
                Tag = definition.UniqueId,
                OnContent = string.Empty,
                OffContent = string.Empty,
                VerticalAlignment = VerticalAlignment.Center
            };
            toggle.Toggled += OnSensorToggled;

            var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            heading.Children.Add(new TextBlock { Text = definition.Name, FontWeight = FontWeights.SemiBold });

            if (!string.IsNullOrWhiteSpace(definition.AutomationIdea))
            {
                var ideaText = $"Automation idea: {definition.AutomationIdea}";
                var idea = new Button
                {
                    Content = new FontIcon
                    {
                        Glyph = "\uE946",
                        FontSize = 12
                    },
                    Padding = new Thickness(5, 1, 5, 1),
                    MinWidth = 24,
                    MinHeight = 24,
                    UseSystemFocusVisuals = true,
                    VerticalAlignment = VerticalAlignment.Center,
                    Flyout = new Flyout
                    {
                        Content = AutomationIdeaText(ideaText)
                    }
                };
                ToolTipService.SetToolTip(idea, new ToolTip { Content = AutomationIdeaText(ideaText) });
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                    idea,
                    $"Show automation idea for {definition.Name}");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(idea, ideaText);
                heading.Children.Add(idea);
            }

            if (definition.Privacy == SensorPrivacy.Sensitive)
            {
                heading.Children.Add(new TextBlock
                {
                    Text = "sensitive",
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"]
                });
            }

            if (LifecycleSensorAdvisory.IsAdvisedSensor(definition.UniqueId))
            {
                // Says up front what the description spells out, so the caveat is
                // visible without reading the whole entry.
                heading.Children.Add(new TextBlock
                {
                    Text = LifecycleSensorAdvisory.Badge,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"]
                });
            }

            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(heading);
            text.Children.Add(new TextBlock
            {
                Text = definition.Description,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
            if (!string.IsNullOrWhiteSpace(definition.ResourceUsage))
            {
                text.Children.Add(new TextBlock
                {
                    Text = $"Impact: {definition.ResourceUsage}",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                        "TextFillColorSecondaryBrush"]
                });
            }
            text.Children.Add(new TextBlock
            {
                Text = previews.TryGetValue(definition.UniqueId, out var value)
                    ? $"Current value: {value}"
                    : "Current value: Unavailable",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });

            if (definition.UniqueId == FrontmostAppSensorSource.FrontmostAppId)
                AddFrontmostAppDetailSetting(text, catalog);

            var row = new Grid { Padding = new Thickness(0, 10, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(text, 0);
            Grid.SetColumn(toggle, 1);
            row.Children.Add(text);
            row.Children.Add(toggle);

            SensorList.Children.Add(row);
        }

        return true;
    }

    private static TextBlock AutomationIdeaText(string text) => new()
    {
        Text = text,
        MaxWidth = 320,
        TextWrapping = TextWrapping.Wrap
    };

    private void AddFrontmostAppDetailSetting(StackPanel container, SensorCatalog catalog)
    {
        var mode = new ComboBox
        {
            Header = "Reported detail",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 6, 0, 0)
        };
        mode.Items.Add(new ComboBoxItem
        {
            Content = "Application name only",
            Tag = FrontmostAppMode.ApplicationName
        });
        mode.Items.Add(new ComboBoxItem
        {
            Content = "Full window title",
            Tag = FrontmostAppMode.FullWindowTitle
        });
        mode.SelectedIndex =
            catalog.Preferences.FrontmostAppMode == FrontmostAppMode.FullWindowTitle ? 1 : 0;
        mode.SelectionChanged += OnFrontmostAppModeChanged;

        container.Children.Add(mode);
        container.Children.Add(new TextBlock
        {
            Text = "Full titles may reveal document names, messages, customer names and websites.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                "SystemFillColorCautionBrush"]
        });
    }

    private async void OnSensorToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressSensorToggle) return;
        if (sender is not ToggleSwitch { Tag: string uniqueId } toggle) return;

        var catalog = _controller.Catalog;
        if (catalog is null) return;

        if (LifecycleSensorAdvisory.RequiresConfirmation(uniqueId, toggle.IsOn, catalog.IsEnabled(uniqueId)))
        {
            var advisory = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = LifecycleSensorAdvisory.Title,
                Content = new TextBlock
                {
                    Text = LifecycleSensorAdvisory.Message,
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText = LifecycleSensorAdvisory.PrimaryButton,
                CloseButtonText = LifecycleSensorAdvisory.CloseButton,
                DefaultButton = ContentDialogButton.Close
            };

            toggle.IsEnabled = false;
            var answer = await advisory.ShowAsync();
            toggle.IsEnabled = true;

            if (answer != ContentDialogResult.Primary || !ReferenceEquals(catalog, _controller.Catalog))
            {
                // Nothing is saved or applied on a cancel: the toggle goes back to
                // where it was and the sensor stays off.
                SetToggleState(toggle, false);
                return;
            }
        }

        if (uniqueId == WinGetUpdateSensorSource.WinGetUpdatesId
            && toggle.IsOn
            && !catalog.IsEnabled(uniqueId))
        {
            toggle.IsEnabled = false;
            var installed = await _controller.IsWinGetModuleInstalledAsync();
            if (!ReferenceEquals(catalog, _controller.Catalog))
            {
                toggle.IsEnabled = true;
                SetToggleState(toggle, false);
                return;
            }

            if (!installed)
            {
                const string installCommand =
                    "Install-Module Microsoft.WinGet.Client -Repository PSGallery "
                    + "-Scope CurrentUser -MinimumVersion 1.29.280";
                var commandBox = new TextBox
                {
                    Header = "Run in Windows PowerShell",
                    Text = installCommand,
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap
                };
                var content = new StackPanel { Spacing = 12 };
                content.Children.Add(new TextBlock
                {
                    Text = "WinGet Updates requires Microsoft's official "
                           + "Microsoft.WinGet.Client PowerShell module version 1.29.280 "
                           + "or newer. The companion will not install executable code "
                           + "automatically. Install the module for your user, then enable "
                           + "the sensor again.",
                    TextWrapping = TextWrapping.Wrap
                });
                content.Children.Add(commandBox);

                var dialog = new ContentDialog
                {
                    XamlRoot = Content.XamlRoot,
                    Title = "WinGet client module required",
                    Content = content,
                    PrimaryButtonText = "Copy command",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close
                };

                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    var package = new DataPackage();
                    package.SetText(installCommand);
                    Clipboard.SetContent(package);
                }

                toggle.IsEnabled = true;
                SetToggleState(toggle, false);
                return;
            }

            toggle.IsEnabled = true;
        }

        catalog.SetEnabled(uniqueId, toggle.IsOn);
        await _controller.ApplySensorChangesAsync();
    }

    private void SetToggleState(ToggleSwitch toggle, bool isOn)
    {
        _suppressSensorToggle = true;
        toggle.IsOn = isOn;
        _suppressSensorToggle = false;
    }

    private async void OnIdleMinutesChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        var catalog = _controller.Catalog;
        if (catalog is null || double.IsNaN(args.NewValue)) return;

        catalog.Preferences.IdleThresholdSeconds = (int)Math.Max(1, args.NewValue) * 60;
        await _controller.ApplySensorChangesAsync();
    }

    private async void OnFrontmostAppModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSensorSettings) return;
        if (sender is not ComboBox modeBox) return;

        var catalog = _controller.Catalog;
        if (catalog is null) return;

        var selected = modeBox.SelectedIndex == 1
            ? FrontmostAppMode.FullWindowTitle
            : FrontmostAppMode.ApplicationName;

        if (selected == FrontmostAppMode.FullWindowTitle
            && catalog.Preferences.FrontmostAppMode != FrontmostAppMode.FullWindowTitle)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "Share full window titles?",
                Content = "Window titles can contain document names, messages, customer names "
                          + "and complete website titles. This value will be sent to your Home "
                          + "Assistant server whenever the sensor reports.",
                PrimaryButtonText = "Use full titles",
                CloseButtonText = "Keep application names",
                DefaultButton = ContentDialogButton.Close
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                _loadingSensorSettings = true;
                modeBox.SelectedIndex = 0;
                _loadingSensorSettings = false;
                return;
            }
        }

        catalog.Preferences.FrontmostAppMode = selected;
        _controller.SaveSensorPreferences();
        await BuildSensorListAsync();
    }

    private void SetSignInBusy(bool busy)
    {
        SignInButton.IsEnabled = !busy;
        UrlBox.IsEnabled = !busy;
        SignInProgress.IsActive = busy;
        if (busy) ConnectError.Visibility = Visibility.Collapsed;
    }

    private void ShowConnectError(string message)
    {
        ConnectError.Text = message;
        ConnectError.Visibility = Visibility.Visible;
    }
}
