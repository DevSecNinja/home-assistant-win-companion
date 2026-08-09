using HaCompanion.Core.Lifecycle;
using HaCompanion.Core.Models;
using HaCompanion.Core.Sensors;
using HaCompanion_App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace HaCompanion_App;

/// <summary>
/// The single application window. Shows a Connect view until a Home Assistant
/// session is established, then a lean Status view. The app is tray-resident:
/// closing the window hides it to the notification area rather than exiting.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly AppController _controller;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _statusTimer;
    private readonly WindowsStartupRegistration _startup = new();
    private readonly bool _startHidden;
    private bool _exiting;
    private bool _connected;
    private int _sensorListBuildVersion;
    private bool _suppressSensorToggle;
    private bool _loadingSensorSettings;
    private bool _loadingStartupSetting;

    public MainWindow(bool startHidden = false)
    {
        InitializeComponent();
        _startHidden = startHidden;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");

        _controller = App.Controller;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _controller.StateChanged += OnStateChanged;

        _statusTimer = _dispatcher.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromSeconds(5);
        _statusTimer.Tick += (_, _) => RefreshBattery();

        AppWindow.Closing += OnWindowClosing;
        Activated += OnFirstActivated;
    }

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

    private async void OnDisconnect(object sender, RoutedEventArgs e)
    {
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

    private async void OnChangeServerUrl(object sender, RoutedEventArgs e)
    {
        ServerChangeError.Visibility = Visibility.Collapsed;
        var urlBox = new TextBox
        {
            Header = "Home Assistant URL",
            Text = _controller.BaseUrl ?? string.Empty
        };
        var validationError = new TextBlock
        {
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                "SystemFillColorCriticalBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(urlBox);
        content.Children.Add(validationError);

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Change server URL",
            Content = content,
            PrimaryButtonText = "Validate and change",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        AppController.ServerUrlChangeResult? changeResult = null;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            args.Cancel = true;
            dialog.IsPrimaryButtonEnabled = false;
            validationError.Visibility = Visibility.Collapsed;

            try
            {
                changeResult = await _controller.ChangeServerUrlAsync(urlBox.Text);
                args.Cancel = false;
            }
            catch (Exception ex)
            {
                validationError.Text = ex.Message;
                validationError.Visibility = Visibility.Visible;
            }
            finally
            {
                dialog.IsPrimaryButtonEnabled = true;
                deferral.Complete();
            }
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary
            || changeResult is null)
        {
            return;
        }

        if (changeResult == AppController.ServerUrlChangeResult.Changed)
        {
            _connected = _controller.State != ConnectionState.Disconnected;
            DisconnectButton.Content = _connected ? "Disconnect" : "Reconnect";
            UpdateNowButton.IsEnabled = _connected;
            if (_connected) _statusTimer.Start();
            else _statusTimer.Stop();
            RefreshStatusFields();
            return;
        }

        try
        {
            var replace = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "Sign in to a different server?",
                Content = "The saved credentials are not valid at this URL. Replacing the "
                          + "server revokes the current session and creates a new Mobile App "
                          + "device after browser sign-in.",
                PrimaryButtonText = "Replace and sign in",
                CloseButtonText = "Keep current server",
                DefaultButton = ContentDialogButton.Close
            };

            if (await replace.ShowAsync() == ContentDialogResult.Primary)
            {
                await _controller.RemoveServerAsync();
                try
                {
                    await _controller.SignInAsync(urlBox.Text);
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
        }
        catch (Exception ex)
        {
            ServerChangeError.Text = "Server change failed: " + ex.Message;
            ServerChangeError.Visibility = Visibility.Visible;
        }
    }

    private void OnShowWindow(object sender, RoutedEventArgs e) => Show();

    private void OnExit(object sender, RoutedEventArgs e)
    {
        _exiting = true;
        TrayIcon.Dispose();
        Application.Current.Exit();
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

        ServerText.Text = _controller.BaseUrl ?? "—";

        var last = _controller.LastSyncedAt;
        LastUpdateText.Text = last is null
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
        TrayIcon.ToolTipText = healthy
            ? "Home Assistant Companion — Healthy"
            : $"Home Assistant Companion — {summary}";
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

    private enum View { Connect, Status, Settings }

    private void ShowView(View view)
    {
        ConnectPanel.Visibility = view == View.Connect ? Visibility.Visible : Visibility.Collapsed;
        StatusPanel.Visibility = view == View.Status ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = view == View.Settings ? Visibility.Visible : Visibility.Collapsed;

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
            || _controller.State is ConnectionState.Disconnected or ConnectionState.AuthError)
        {
            return false;
        }

        SensorList.Children.Clear();
        _loadingSensorSettings = true;
        IdleMinutesBox.Value = Math.Max(1, catalog.Preferences.IdleThresholdSeconds / 60);
        FrontmostAppModeBox.SelectedIndex =
            catalog.Preferences.FrontmostAppMode == FrontmostAppMode.FullWindowTitle ? 1 : 0;
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
            text.Children.Add(new TextBlock
            {
                Text = previews.TryGetValue(definition.UniqueId, out var value)
                    ? $"Current value: {value}"
                    : "Current value: Unavailable",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });

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

        var catalog = _controller.Catalog;
        if (catalog is null) return;

        var selected = FrontmostAppModeBox.SelectedIndex == 1
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
                FrontmostAppModeBox.SelectedIndex = 0;
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
