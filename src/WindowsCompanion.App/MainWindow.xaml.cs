using System.Runtime.InteropServices;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WindowsCompanion.Core.App;
using WindowsCompanion.Core.Models;
using WindowsCompanion_App.Services;

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
    private const int MaximumWindowWidth = 960;

    private readonly AppController _controller;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _statusTimer;
    private readonly DispatcherQueueTimer _sensorPreviewTimer;
    private readonly IStartupRegistration _startup;
    private readonly RestartManagerShutdownMonitor _restartManagerShutdown;
    private readonly MainWindowActivation _windowActivation;
    private readonly nint _windowHandle;
    private readonly bool _startHidden;
    private bool _exiting;
    private bool _connected;
    private View _currentView;

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
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dpi = GetDpiForWindow(_windowHandle);
        AppWindow.Resize(new SizeInt32(
            ScaleForDpi(InitialWindowWidth, dpi),
            ScaleForDpi(InitialWindowHeight, dpi)));
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = ScaleForDpi(MinimumWindowWidth, dpi);
            presenter.PreferredMinimumHeight = ScaleForDpi(MinimumWindowHeight, dpi);
            presenter.PreferredMaximumWidth = ScaleForDpi(MaximumWindowWidth, dpi);
            presenter.IsMaximizable = false;
        }

        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _controller.StateChanged += OnStateChanged;
        _controller.RouteChanged += OnRouteChanged;
        _controller.UpdateStateChanged += OnUpdateStateChanged;
        _controller.InstallStateChanged += OnInstallStateChanged;
        UpdateBanner.Closed += (_, _) => _showingLastInstallResult = false;
        ApplyUpdateState(_controller.UpdateState);
        ShowLastInstallResultIfAny();

#if DEBUG
        if (App.TestLaunchOptions is not { SuppressTrayLeftClick: true })
            TrayIcon.LeftClickCommand = TrayShowWindowCommand;
#else
        TrayIcon.LeftClickCommand = TrayShowWindowCommand;
#endif
        TrayIcon.DoubleClickCommand = TrayShowWindowCommand;
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

        _sensorPreviewTimer = _dispatcher.CreateTimer();
        _sensorPreviewTimer.Interval = TimeSpan.FromSeconds(2);
        _sensorPreviewTimer.IsRepeating = true;
        _sensorPreviewTimer.Tick += OnSensorPreviewTimerTick;

        AppWindow.Closing += OnWindowClosing;
        AppWindow.Changed += OnAppWindowChanged;
        Activated += OnWindowPresentationChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Activated += OnFirstActivated;
        _restartManagerShutdown = new RestartManagerShutdownMonitor(
            _windowHandle,
            () => ((App)Application.Current).RequestShutdown(AppShutdownReason.RestartManager));
    }

    private static int ScaleForDpi(int logicalPixels, uint dpi) =>
        (int)Math.Round(logicalPixels * Math.Max(96u, dpi) / 96d);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

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
                : _controller.BaseUrl?.TrimEnd('/') ?? "—";
            UpdateHealth();
            RefreshPreferencesSummary();
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
            RefreshPreferencesSummary();
        });

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
        StopSensorPreviewRefresh();
        AppWindow.Hide();
        _statusTimer.Stop();
        _controller.StateChanged -= OnStateChanged;
        _controller.RouteChanged -= OnRouteChanged;
        _controller.UpdateStateChanged -= OnUpdateStateChanged;
        _controller.InstallStateChanged -= OnInstallStateChanged;
        AppWindow.Changed -= OnAppWindowChanged;
        Activated -= OnWindowPresentationChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
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
        StopSensorPreviewRefresh();
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

    void IMainWindowActivationTarget.BringToFront()
    {
        AppWindow.MoveInZOrderAtTop();
        SetForegroundWindow(_windowHandle);
    }

    void IMainWindowActivationTarget.Activate() => Activate();

    private void ShowPanel(bool connected)
    {
        ShowView(connected ? View.Status : View.Connect);
    }

    private enum View { Connect, Status, Preferences, Sensors, Connection }

    private void ShowView(View view)
    {
        _currentView = view;
        ConnectPanel.Visibility = view == View.Connect ? Visibility.Visible : Visibility.Collapsed;
        StatusPanel.Visibility = view == View.Status ? Visibility.Visible : Visibility.Collapsed;
        PreferencesPanel.Visibility = view == View.Preferences ? Visibility.Visible : Visibility.Collapsed;
        SensorsPanel.Visibility = view == View.Sensors ? Visibility.Visible : Visibility.Collapsed;
        ConnectionPanel.Visibility = view == View.Connection ? Visibility.Visible : Visibility.Collapsed;
        UpdateBanner.Visibility = view is View.Status or View.Connect
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (view == View.Connect)
        {
            ConnectError.Visibility = Visibility.Collapsed;
            _statusTimer.Stop();
        }

        UpdateSensorPreviewRefreshState(refreshImmediately: false);
    }

    private void OnAppWindowChanged(
        Microsoft.UI.Windowing.AppWindow sender,
        Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
    {
        if (args.DidVisibilityChange || args.DidPresenterChange)
            UpdateSensorPreviewRefreshState(refreshImmediately: true);
    }

    private void OnWindowPresentationChanged(object sender, WindowActivatedEventArgs args) =>
        UpdateSensorPreviewRefreshState(refreshImmediately: true);

    private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs args)
    {
        if (_exiting) return;
        _dispatcher.TryEnqueue(() =>
        {
            if (_exiting) return;

            if (args.Mode == Microsoft.Win32.PowerModes.Suspend)
                StopSensorPreviewRefresh();
            else if (args.Mode == Microsoft.Win32.PowerModes.Resume)
                UpdateSensorPreviewRefreshState(refreshImmediately: true);
        });
    }

    private bool IsSensorPreviewPresented() =>
        SensorPreviewPresentation.IsActive(
            _currentView == View.Sensors,
            AppWindow.IsVisible,
            AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter
            {
                State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized
            },
            _exiting);

    private void UpdateSensorPreviewRefreshState(bool refreshImmediately)
    {
        if (!IsSensorPreviewPresented())
        {
            StopSensorPreviewRefresh();
            return;
        }

        var wasRunning = _sensorPreviewTimer.IsRunning;
        if (!wasRunning) _sensorPreviewTimer.Start();
        if (refreshImmediately && !wasRunning)
            _ = RefreshSensorPreviewsAsync(retryWhenBusy: true);
    }

    private void StopSensorPreviewRefresh()
    {
        _sensorPreviewTimer.Stop();
        _sensorPreviewRefreshPending = false;
        _sensorPreviewCancellation.CancelList();
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
