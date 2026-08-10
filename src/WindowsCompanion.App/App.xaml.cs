using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using WindowsCompanion.Core.App;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WindowsCompanion_App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private const string InstanceMutexName = @"Local\WindowsCompanion.Instance";
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
    private static string ShutdownSignalName =>
        $@"Local\WindowsCompanion.Shutdown.{Environment.ProcessId}";

    private Window? _window;
    private DispatcherQueue? _dispatcher;
    private EventWaitHandle? _shutdownSignal;
    private RegisteredWaitHandle? _shutdownRegistration;
    private Mutex? _instanceMutex;
    private bool _notificationsRegistered;
    private int _shutdownStarted;

    /// <summary>Shared coordinator for the OAuth session and Home Assistant connection.</summary>
    public static AppController Controller { get; private set; } = null!;
#if DEBUG
    internal static TestAppLaunchOptions? TestLaunchOptions { get; private set; }
#endif
    
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
#if DEBUG
        TestLaunchOptions = TestAppLaunchOptions.Parse(Environment.GetCommandLineArgs());
        Controller = TestLaunchOptions is null
            ? new AppController()
            : TestAppComposition.Create(TestLaunchOptions);
        _instanceMutex = new Mutex(
            initiallyOwned: false,
            TestLaunchOptions?.MutexName ?? InstanceMutexName);
#else
        Controller = new AppController();
        _instanceMutex = new Mutex(initiallyOwned: false, InstanceMutexName);
#endif
        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // Register for Windows toast notifications (works unpackaged).
        AppNotificationManager.Default.Register();
        _notificationsRegistered = true;

        var startupLaunch = StartupCommand.IsStartupLaunch(Environment.GetCommandLineArgs());
        var startHidden = startupLaunch && Controller.HasSavedSession;
        _window = new MainWindow(startHidden);
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        RegisterShutdownSignal();
        _window.Activate();
    }

    private void RegisterShutdownSignal()
    {
        _shutdownSignal = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ShutdownSignalName);
        _shutdownRegistration = ThreadPool.RegisterWaitForSingleObject(
            _shutdownSignal,
            static (state, timedOut) =>
            {
                if (!timedOut) ((App)state!).RequestExternalShutdown();
            },
            this,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    private void RequestExternalShutdown()
    {
        _dispatcher?.TryEnqueue(() =>
        {
            if (_window is MainWindow window)
                _ = window.RequestExitAsync();
        });
    }

    internal async Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0) return;

        _shutdownRegistration?.Unregister(null);
        _shutdownRegistration = null;
        _shutdownSignal?.Dispose();
        _shutdownSignal = null;

        try
        {
            await Controller.DisposeAsync().AsTask().WaitAsync(ShutdownTimeout);
        }
        catch (TimeoutException ex)
        {
            Trace.TraceError("Companion shutdown cleanup exceeded {0}: {1}", ShutdownTimeout, ex);
        }
        catch (Exception ex)
        {
            Trace.TraceError("Companion shutdown cleanup failed: {0}", ex);
        }
        finally
        {
            if (_notificationsRegistered)
            {
                try
                {
                    AppNotificationManager.Default.Unregister();
                }
                catch (Exception ex)
                {
                    Trace.TraceError("App notification shutdown failed: {0}", ex);
                }

                _notificationsRegistered = false;
            }

            _instanceMutex?.Dispose();
            _instanceMutex = null;

            // WinUI's Application.Exit can leave this unpackaged tray process
            // alive, and has produced CoreMessaging stowed exceptions during
            // teardown. All owned resources are released above; terminate the
            // process explicitly so Exit always means Exit.
            Environment.Exit(0);
        }
    }
}
