using HaCompanion.Core.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

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
    private bool _exiting;

    public MainWindow()
    {
        InitializeComponent();

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
        try
        {
            var resumed = await _controller.TryResumeAsync();
            ShowPanel(resumed);
            if (resumed)
            {
                RefreshBattery();
                _statusTimer.Start();
            }
        }
        catch
        {
            ShowPanel(false);
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
                ShowPanel(false);
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
        _statusTimer.Stop();
        try
        {
            await _controller.DisconnectAsync();
        }
        catch
        {
            // Ignore; local state is cleared regardless.
        }
        ShowPanel(false);
        Show();
    }

    private void OnOpenHomeAssistant(object sender, RoutedEventArgs e) => _controller.OpenHomeAssistant();

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
        AppWindow.Show();
        AppWindow.MoveInZOrderAtTop();
    }

    private void RefreshBattery()
    {
        var status = _controller.GetSystemStatus();
        BatteryText.Text = status.HasBattery
            ? $"{status.BatteryPercent}% ({status.BatteryStateString})"
            : "No battery (desktop)";
    }

    private void ShowPanel(bool connected)
    {
        ConnectPanel.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        StatusPanel.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        if (!connected)
        {
            ConnectError.Visibility = Visibility.Collapsed;
            _statusTimer.Stop();
        }
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
