using WindowsCompanion.Core.App;
using WindowsCompanion.Core.Lifecycle;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;
using WindowsCompanion.Core.Updates;
using WindowsCompanion_App.Services;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace WindowsCompanion_App;

/// <summary>
/// The single application window. Shows a Connect view until a Home Assistant
/// session is established, then a lean Status view. The app is tray-resident:
/// closing the window hides it to the notification area rather than exiting.
/// </summary>
public sealed partial class MainWindow : Window, IMainWindowActivationTarget
{
    private const int InitialWindowWidth = 720;
    private const int InitialWindowHeight = 820;
    private const int MinimumWindowWidth = 520;
    private const int MinimumWindowHeight = 600;

    private readonly AppController _controller;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _statusTimer;
    private readonly IStartupRegistration _startup;
    private readonly RestartManagerShutdownMonitor _restartManagerShutdown;
    private readonly MainWindowActivation _windowActivation;
    private readonly bool _startHidden;
    private bool _exiting;
    private bool _connected;
    private int _sensorListBuildVersion;
    private bool _suppressSensorToggle;
    private readonly object _sensorPreviewCancellationGate = new();
    private CancellationTokenSource? _sensorListPreviewCancellation;
    private readonly Dictionary<string, CancellationTokenSource> _sensorPreviewCancellations =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> _sensorPreviewTexts =
        new(StringComparer.Ordinal);
    private readonly List<Control> _sensorSettingControls = [];
    private bool _loadingSensorSettings;
    private bool _loadingStartupSetting;
    private bool _settingsActionBusy;
    private int _connectionActionRunning;
    private View _sensorReturnView = View.Status;

    public ICommand TrayOpenHomeAssistantCommand { get; }
    public ICommand TrayUpdateCommand { get; }
    public ICommand TrayShowWindowCommand { get; }
    public ICommand TrayDisconnectCommand { get; }
    public ICommand TrayExitCommand { get; }

    public MainWindow(
        bool startHidden = false,
        IStartupRegistration? startupRegistration = null)
    {
        _controller = App.Controller;
        _startup = startupRegistration ?? new WindowsStartupRegistration();
        _windowActivation = new MainWindowActivation(this);
        _updateActions = new UpdateUiActions(
            ActivateMainWindow,
            _controller.CheckForUpdates,
            OpenReleasePage);
        TrayOpenHomeAssistantCommand = new ActionCommand(
            () => DispatchTrayAction(_controller.OpenHomeAssistant));
        TrayUpdateCommand = new ActionCommand(
            () => DispatchTrayAction(HandleUpdateTrayAction));
        TrayShowWindowCommand = new ActionCommand(
            () => DispatchTrayAction(ActivateMainWindow));
        TrayDisconnectCommand = new ActionCommand(
            () => DispatchTrayAction(() => OnDisconnect(this, new RoutedEventArgs())));
        TrayExitCommand = new ActionCommand(
            () => ((App)Application.Current).RequestShutdown(AppShutdownReason.TrayMenu));

        InitializeComponent();
        _startHidden = startHidden;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dpi = GetDpiForWindow(windowHandle);
        AppWindow.Resize(new SizeInt32(
            ScaleForDpi(InitialWindowWidth, dpi),
            ScaleForDpi(InitialWindowHeight, dpi)));
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = ScaleForDpi(MinimumWindowWidth, dpi);
            presenter.PreferredMinimumHeight = ScaleForDpi(MinimumWindowHeight, dpi);
        }

        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _controller.StateChanged += OnStateChanged;
        _controller.RouteChanged += OnRouteChanged;
        _controller.UpdateStateChanged += OnUpdateStateChanged;
        ApplyUpdateState(_controller.UpdateState);

        var showWindowCommand = new XamlUICommand();
        showWindowCommand.ExecuteRequested += (_, _) => ActivateMainWindow();
        TrayIcon.LeftClickCommand = showWindowCommand;
        TrayIcon.DoubleClickCommand = showWindowCommand;
#if DEBUG
        if (App.TestLaunchOptions is { } testOptions)
            TrayIcon.ToolTipText = testOptions.TrayIdentity;
#endif

        // Kept in Core so the demo warning reads the same wherever it is shown.
        DemoBanner.Title = DemoSession.Title;
        DemoBanner.Message = DemoSession.Message;

        _statusTimer = _dispatcher.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromSeconds(5);
        _statusTimer.Tick += (_, _) => RefreshBattery();

        AppWindow.Closing += OnWindowClosing;
        Activated += OnFirstActivated;
        _restartManagerShutdown = new RestartManagerShutdownMonitor(
            windowHandle,
            () => ((App)Application.Current).RequestShutdown(AppShutdownReason.RestartManager));
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
            _connected = resumed;
            ShowPanel(resumed);
            // Only offered once it is settled that there is no session to resume:
            // starting a demo alongside a connection in flight would make the app
            // claim it sends nothing while it is connecting.
            DemoModeButton.IsEnabled = !resumed;
            if (!resumed && _startHidden) ActivateMainWindow();
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
            DemoModeButton.IsEnabled = true;
            if (_startHidden) ActivateMainWindow();
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
            if (state == ConnectionState.Connected)
                _connected = true;
            else if (state is ConnectionState.Disconnected or ConnectionState.AuthError)
                _connected = false;

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
                ActivateMainWindow();
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
                DisconnectButton.Content = "Stop connection";
                SyncSensorsButton.IsEnabled = true;
                ChooseSensorsButton.IsEnabled = true;
                IdleMinutesBox.IsEnabled = true;
                _statusTimer.Start();
                ShowSettingsActionStatus("Reconnected to Home Assistant.", true);
            }
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
        DisconnectButton.Content = "Stop connection";
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

    private void OnOpenHomeAssistant(object sender, RoutedEventArgs e) => _controller.OpenHomeAssistant();

    private void DispatchTrayAction(Action action)
    {
        if (_exiting) return;
        _dispatcher.TryEnqueue(() =>
        {
            if (!_exiting) action();
        });
    }

    internal void BeginShutdown()
    {
        if (_exiting) return;

        _exiting = true;
        AppWindow.Hide();
        _statusTimer.Stop();
        _controller.StateChanged -= OnStateChanged;
        _controller.RouteChanged -= OnRouteChanged;
        _controller.UpdateStateChanged -= OnUpdateStateChanged;
        _restartManagerShutdown.Dispose();
        TrayIcon.Dispose();
    }

    internal void CloseForShutdown()
    {
        AppWindow.Closing -= OnWindowClosing;
        Close();
    }

    private void OnWindowClosing(Microsoft.UI.Windowing.AppWindow sender,
        Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_exiting) return;
        args.Cancel = true;
        AppWindow.Hide();
    }

    private void ActivateMainWindow()
    {
        if (_exiting) return;
        RefreshStartupSetting();
        _windowActivation.Activate();
    }

    bool IMainWindowActivationTarget.IsMinimized =>
        AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter
        {
            State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized
        };

    void IMainWindowActivationTarget.Show() => AppWindow.Show();

    void IMainWindowActivationTarget.Restore()
    {
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            presenter.Restore();
    }

    void IMainWindowActivationTarget.BringToFront() => AppWindow.MoveInZOrderAtTop();

    void IMainWindowActivationTarget.Activate() => Activate();

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

    private void ShowPanel(bool connected)
    {
        ShowView(connected ? View.Status : View.Connect);
    }

    private enum View { Connect, Status, Preferences, Sensors, Connection }

    private void ShowView(View view)
    {
        ConnectPanel.Visibility = view == View.Connect ? Visibility.Visible : Visibility.Collapsed;
        StatusPanel.Visibility = view == View.Status ? Visibility.Visible : Visibility.Collapsed;
        PreferencesPanel.Visibility = view == View.Preferences ? Visibility.Visible : Visibility.Collapsed;
        SensorsPanel.Visibility = view == View.Sensors ? Visibility.Visible : Visibility.Collapsed;
        ConnectionPanel.Visibility = view == View.Connection ? Visibility.Visible : Visibility.Collapsed;

        if (view == View.Connect)
        {
            ConnectError.Visibility = Visibility.Collapsed;
            _statusTimer.Stop();
        }
    }

    private async void OnShowSensors(object sender, RoutedEventArgs e)
    {
        _sensorReturnView = View.Status;
        if (await BuildSensorListAsync())
            ShowView(View.Sensors);
    }

    private async void OnShowSensorsFromSettings(object sender, RoutedEventArgs e)
    {
        _sensorReturnView = View.Preferences;
        if (await BuildSensorListAsync())
            ShowView(View.Sensors);
    }

    private void OnCloseSensors(object sender, RoutedEventArgs e)
    {
        CancelSensorPreviews();
        ShowView(_sensorReturnView);
    }

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
        DisconnectButton.Content = _connected ? "Stop connection" : "Reconnect";
        SettingsActionStatus.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Renders one toggle per catalog sensor. Built in code rather than bound so the
    /// list always reflects whatever sources the controller actually wired up.
    /// </summary>
    private async Task<bool> BuildSensorListAsync()
    {
        var catalog = _controller.Catalog;
        if (catalog is null) return false;

        var buildVersion = ++_sensorListBuildVersion;
        using var previewCancellation = BeginSensorListPreview();
        IReadOnlyDictionary<string, string> previews;
        try
        {
            previews = await _controller.PreviewSensorsAsync(previewCancellation.Token);
        }
        catch (OperationCanceledException) when (previewCancellation.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            EndSensorListPreview(previewCancellation);
        }

        if (buildVersion != _sensorListBuildVersion
            || !ReferenceEquals(catalog, _controller.Catalog)
            || (!_controller.IsDemoMode
                && _controller.State is ConnectionState.Disconnected or ConnectionState.AuthError))
        {
            return false;
        }

        SensorList.Children.Clear();
        _sensorPreviewTexts.Clear();
        _sensorSettingControls.Clear();
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
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(
                toggle,
                $"Sensors.Toggle.{definition.UniqueId}");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                toggle,
                $"{definition.Name} enabled");
            toggle.Toggled += OnSensorToggled;
            _sensorSettingControls.Add(toggle);

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
            var previewText = new TextBlock
            {
                Text = previews.TryGetValue(definition.UniqueId, out var value)
                    ? $"Current value: {value}"
                    : definition.Privacy == SensorPrivacy.Sensitive
                      && !catalog.IsEnabled(definition.UniqueId)
                        ? "Current value: read only once you enable this sensor"
                        : "Current value: Unavailable",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };
            text.Children.Add(previewText);
            _sensorPreviewTexts[definition.UniqueId] = previewText;

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
        _sensorSettingControls.Add(mode);

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
            PrepareDialog(advisory);

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
            var capability = await _controller.ProbeWinGetCapabilityAsync();
            if (!ReferenceEquals(catalog, _controller.Catalog))
            {
                toggle.IsEnabled = true;
                SetToggleState(toggle, false);
                return;
            }

            if (!capability.IsReady)
            {
                capability = await ShowWinGetCapabilityDialogAsync(capability);
                toggle.IsEnabled = true;
                if (!capability.IsReady)
                {
                    SetToggleState(toggle, false);
                    return;
                }

                if (!ReferenceEquals(catalog, _controller.Catalog))
                {
                    SetToggleState(toggle, false);
                    return;
                }
            }

            toggle.IsEnabled = true;
        }

        var wasEnabled = catalog.IsEnabled(uniqueId);
        toggle.IsEnabled = false;
        using var previewCancellation = BeginSensorPreview(uniqueId);
        Exception? refreshFailure = null;
        string? refreshedPreview = null;
        try
        {
            try
            {
                refreshedPreview = await catalog.SetEnabledAndRefreshAsync(
                    uniqueId,
                    toggle.IsOn,
                    previewCancellation.Token);
            }
            catch (OperationCanceledException) when (previewCancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                if (catalog.IsEnabled(uniqueId) == toggle.IsOn)
                {
                    refreshFailure = ex;
                }
                else
                {
                    SetToggleState(toggle, wasEnabled);
                    ShowSensorPreviewError(uniqueId, "Could not update sensor: " + ex.Message);
                    return;
                }
            }

            if (!toggle.IsOn
                && _sensorPreviewTexts.TryGetValue(uniqueId, out var disabledPreview))
            {
                var definition = catalog.Definitions.First(candidate =>
                    string.Equals(candidate.UniqueId, uniqueId, StringComparison.Ordinal));
                disabledPreview.Text = "Current value: " + definition.DisabledPreview;
                disabledPreview.Foreground =
                    (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                        "TextFillColorSecondaryBrush"];
            }

            try
            {
                await _controller.ApplySensorChangesAsync();
            }
            catch (Exception ex)
            {
                catalog.SetEnabled(uniqueId, wasEnabled);
                SetToggleState(toggle, wasEnabled);
                ShowSensorPreviewError(uniqueId, "Could not update sensor: " + ex.Message);
                return;
            }

            if (!ReferenceEquals(catalog, _controller.Catalog)
                || previewCancellation.IsCancellationRequested)
            {
                return;
            }

            if (refreshFailure is not null)
            {
                ShowSensorPreviewError(uniqueId, "Refresh failed: " + refreshFailure.Message);
                return;
            }

            if (!ReferenceEquals(catalog, _controller.Catalog)
                || catalog.IsEnabled(uniqueId) != toggle.IsOn)
            {
                return;
            }

            if (_sensorPreviewTexts.TryGetValue(uniqueId, out var previewText))
            {
                previewText.Text = $"Current value: {refreshedPreview ?? "Unavailable"}";
                previewText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                    "TextFillColorSecondaryBrush"];
            }
        }
        catch (OperationCanceledException) when (previewCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ShowSensorPreviewError(uniqueId, "Refresh failed: " + ex.Message);
        }
        finally
        {
            EndSensorPreview(uniqueId, previewCancellation);
            if (ReferenceEquals(catalog, _controller.Catalog))
                toggle.IsEnabled = !_settingsActionBusy;
        }
    }

    private async Task<WinGetCapabilityResult> ShowWinGetCapabilityDialogAsync(
        WinGetCapabilityResult capability)
    {
        while (!capability.IsReady)
        {
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock
            {
                Text = capability.Message,
                TextWrapping = TextWrapping.Wrap
            });

            if (capability.CanInstallOrRepair)
            {
                content.Children.Add(new TextBlock
                {
                    Text = "The companion will not install executable code automatically. "
                           + "Run the command below as the same Windows user, then return here "
                           + "and select Recheck.",
                    TextWrapping = TextWrapping.Wrap
                });
                content.Children.Add(new TextBox
                {
                    Header = "Run in PowerShell",
                    Text = PowerShellWinGetUpdateProvider.InstallCommand,
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "WinGet client module unavailable",
                Content = content,
                PrimaryButtonText = "Recheck",
                SecondaryButtonText = capability.CanInstallOrRepair ? "Copy command" : null,
                CloseButtonText = "Not now",
                DefaultButton = ContentDialogButton.Primary
            };

            var answer = await dialog.ShowAsync();
            if (answer == ContentDialogResult.None) return capability;
            if (answer == ContentDialogResult.Secondary)
            {
                var package = new DataPackage();
                package.SetText(PowerShellWinGetUpdateProvider.InstallCommand);
                Clipboard.SetContent(package);
                continue;
            }

            capability = await _controller.ProbeWinGetCapabilityAsync();
        }

        return capability;
    }

    private CancellationTokenSource BeginSensorListPreview()
    {
        var next = new CancellationTokenSource();
        CancellationTokenSource? previous;
        lock (_sensorPreviewCancellationGate)
        {
            previous = _sensorListPreviewCancellation;
            _sensorListPreviewCancellation = next;
        }

        previous?.Cancel();
        return next;
    }

    private void EndSensorListPreview(CancellationTokenSource completed)
    {
        lock (_sensorPreviewCancellationGate)
        {
            if (ReferenceEquals(_sensorListPreviewCancellation, completed))
                _sensorListPreviewCancellation = null;
        }
    }

    private CancellationTokenSource BeginSensorPreview(string uniqueId)
    {
        var next = new CancellationTokenSource();
        CancellationTokenSource? previous = null;
        lock (_sensorPreviewCancellationGate)
        {
            _sensorPreviewCancellations.Remove(uniqueId, out previous);
            _sensorPreviewCancellations[uniqueId] = next;
        }

        previous?.Cancel();
        return next;
    }

    private void EndSensorPreview(string uniqueId, CancellationTokenSource completed)
    {
        lock (_sensorPreviewCancellationGate)
        {
            if (_sensorPreviewCancellations.TryGetValue(uniqueId, out var current)
                && ReferenceEquals(current, completed))
            {
                _sensorPreviewCancellations.Remove(uniqueId);
            }
        }
    }

    private void CancelSensorPreviews()
    {
        CancellationTokenSource? listPreview;
        List<CancellationTokenSource> rowPreviews;
        lock (_sensorPreviewCancellationGate)
        {
            listPreview = _sensorListPreviewCancellation;
            _sensorListPreviewCancellation = null;
            rowPreviews = [.. _sensorPreviewCancellations.Values];
            _sensorPreviewCancellations.Clear();
        }

        listPreview?.Cancel();
        foreach (var cancellation in rowPreviews)
            cancellation.Cancel();
    }

    private void ShowSensorPreviewError(string uniqueId, string message)
    {
        if (!_sensorPreviewTexts.TryGetValue(uniqueId, out var previewText)) return;

        previewText.Text = "Current value: " + message;
        previewText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            "SystemFillColorCautionBrush"];
    }

    private void SetToggleState(ToggleSwitch toggle, bool isOn)
    {
        _suppressSensorToggle = true;
        toggle.IsOn = isOn;
        _suppressSensorToggle = false;
    }

    private async void OnIdleMinutesChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loadingSensorSettings) return;
        var catalog = _controller.Catalog;
        if (catalog is null || double.IsNaN(args.NewValue)) return;

        catalog.Preferences.IdleThresholdSeconds = (int)Math.Max(1, args.NewValue) * 60;
        try
        {
            await _controller.ApplySensorChangesAsync();
            ShowSettingsActionStatus(
                _controller.IsDemoMode
                    ? "Idle threshold updated for this demo."
                    : "Idle threshold saved and synced.",
                true);
        }
        catch (Exception ex)
        {
            ShowSettingsActionStatus(
                "Could not save or sync the idle threshold: " + ex.Message,
                false);
        }
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

    private void ReconcileConnectionControlsAfterFailure()
    {
        _connected = _controller.State
            is not (ConnectionState.Disconnected or ConnectionState.AuthError);
        DisconnectButton.Content = _connected ? "Stop connection" : "Reconnect";
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

    private void ShowSettingsActionStatus(string message, bool positive)
    {
        var messageChanged = !string.Equals(
            SettingsActionStatus.Text,
            message,
            StringComparison.Ordinal);
        SettingsActionStatus.Text = message;
        SettingsActionStatus.Foreground =
            (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                positive ? "SystemFillColorSuccessBrush" : "SystemFillColorCautionBrush"];
        SettingsActionStatus.Visibility = Visibility.Visible;
        if (messageChanged)
        {
            var peer = FrameworkElementAutomationPeer.FromElement(SettingsActionStatus)
                       ?? FrameworkElementAutomationPeer.CreatePeerForElement(
                           SettingsActionStatus);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
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
                Title = _controller.IsDemoMode
                    ? "Show full window titles locally?"
                    : "Share full window titles?",
                Content = "Window titles can contain document names, messages, customer names "
                          + (_controller.IsDemoMode
                              ? "and complete website titles. In demo mode, this value is shown "
                                + "only on this device and is not saved or sent."
                              : "and complete website titles. This value will be sent to your Home "
                                + "Assistant server whenever the sensor reports."),
                PrimaryButtonText = "Use full titles",
                CloseButtonText = "Keep application names",
                DefaultButton = ContentDialogButton.Close
            };
            PrepareDialog(dialog);

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

    private static void PrepareDialog(ContentDialog dialog)
    {
        dialog.PrimaryButtonStyle = DialogButtonStyle("Dialog.Primary");
        dialog.CloseButtonStyle = DialogButtonStyle("Dialog.Cancel");
        dialog.SecondaryButtonStyle = DialogButtonStyle("Dialog.Cancel");
        dialog.Opened += (_, _) =>
        {
            foreach (var button in Descendants(dialog).OfType<Button>())
            {
                if (string.Equals(button.Name, "PrimaryButton", StringComparison.Ordinal)
                    || Equals(button.Content, dialog.PrimaryButtonText))
                {
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(
                        button,
                        "Dialog.Primary");
                }
                else if (string.Equals(button.Name, "CloseButton", StringComparison.Ordinal)
                         || string.Equals(button.Name, "SecondaryButton", StringComparison.Ordinal)
                         || Equals(button.Content, dialog.CloseButtonText)
                         || Equals(button.Content, dialog.SecondaryButtonText))
                {
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(
                        button,
                        "Dialog.Cancel");
                }
            }
        };
    }

    private static Style DialogButtonStyle(string automationId)
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(
            Microsoft.UI.Xaml.Automation.AutomationProperties.AutomationIdProperty,
            automationId));
        return style;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
