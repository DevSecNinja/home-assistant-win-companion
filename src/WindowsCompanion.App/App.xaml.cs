using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using WindowsCompanion.Core.App;
using WindowsCompanion_App.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WindowsCompanion_App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private const string InstanceMutexName = @"Local\WindowsCompanion.Instance";
    private static string ShutdownSignalName =>
        $@"Local\WindowsCompanion.Shutdown.{Environment.ProcessId}";
    private Window? _window;
    private DispatcherQueue? _dispatcher;
    private EventWaitHandle? _shutdownSignal;
    private RegisteredWaitHandle? _shutdownRegistration;
    private Mutex? _instanceMutex;
    private bool _notificationsRegistered;
    private int _shutdownStarted;
    private readonly ILoggerFactory _appLoggerFactory;
    private readonly ILogger<App> _log;

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
        _appLoggerFactory = TestLaunchOptions is null
            ? CreateProductionLoggerFactory()
            : TestAppComposition.CreateLoggerFactory(TestLaunchOptions);
        _log = _appLoggerFactory.CreateLogger<App>();
        Controller = TestLaunchOptions is null
            ? new AppController()
            : TestAppComposition.Create(TestLaunchOptions, _appLoggerFactory);
        _instanceMutex = new Mutex(
            initiallyOwned: false,
            TestLaunchOptions?.MutexName ?? InstanceMutexName);
#else
        _appLoggerFactory = CreateProductionLoggerFactory();
        _log = _appLoggerFactory.CreateLogger<App>();
        Controller = new AppController();
        _instanceMutex = new Mutex(initiallyOwned: false, InstanceMutexName);
#endif
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    /// <summary>
    /// Last-resort diagnostic hook for exceptions that escape UI event handlers
    /// and dispatcher callbacks. Without this, an uncaught exception on the UI
    /// thread (for example the tray-icon resource load this was added alongside)
    /// surfaces as an unrecoverable native crash (STATUS_STOWED_EXCEPTION via
    /// combase.dll) with nothing logged before the process dies.
    /// </summary>
    /// <remarks>
    /// This only logs; it deliberately leaves <c>e.Handled</c> unset. The failing
    /// handler or callback has already aborted, possibly after partially mutating
    /// application or connection state, so continuing past an unrecognized
    /// exception risks a silently corrupted process rather than a clean crash.
    /// Known, recoverable failures (such as a missing icon file) should keep
    /// being caught at their own operation boundary - see MainWindow.ApplyTrayIcon
    /// - where the surrounding state is actually known to be safe to continue from.
    /// </remarks>
    private void OnUnhandledException(
        object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _log.LogCritical(e.Exception, "Unhandled UI exception.");
    }

    private static ILoggerFactory CreateProductionLoggerFactory() =>
        LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new FileLoggerProvider(LogLevel.Debug));
            builder.SetMinimumLevel(LogLevel.Debug);
        });

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // Register for Windows toast notifications (works unpackaged).
        AppNotificationManager.Default.Register();
        _notificationsRegistered = true;
        Controller.StartUpdateCheck();

        var startupLaunch = StartupCommand.IsStartupLaunch(Environment.GetCommandLineArgs());
        var startHidden = startupLaunch && Controller.HasSavedSession;
#if DEBUG
        _window = new MainWindow(
            startHidden,
            TestLaunchOptions is null ? null : new DisabledStartupRegistration());
#else
        _window = new MainWindow(startHidden);
#endif
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
        RequestShutdown(AppShutdownReason.ExternalRequest);
    }

    internal void RequestShutdown(AppShutdownReason reason)
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null)
        {
            _log.LogError("Shutdown requested by {Reason}, but the UI dispatcher is unavailable.", reason);
            return;
        }

        if (!dispatcher.TryEnqueue(() => _ = ShutdownAsync(reason)))
            _log.LogError("Shutdown requested by {Reason}, but it could not be queued.", reason);
    }

    private async Task ShutdownAsync(AppShutdownReason reason)
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0) return;

        _log.LogInformation("Application shutdown initiated by {Reason}.", reason);
        _shutdownRegistration?.Unregister(null);
        _shutdownRegistration = null;
        _shutdownSignal?.Dispose();
        _shutdownSignal = null;

        try
        {
            if (_window is MainWindow window)
                window.BeginShutdown();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Window and tray shutdown failed.");
        }

        try
        {
            await Controller.DisposeAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Background service shutdown failed.");
        }

        if (_notificationsRegistered)
        {
            try
            {
                AppNotificationManager.Default.Unregister();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "App notification shutdown failed.");
            }

            _notificationsRegistered = false;
        }

        try
        {
            await RunOnDispatcherAsync(() =>
            {
                if (_window is MainWindow window)
                    window.CloseForShutdown();
                _window = null;

                _instanceMutex?.Dispose();
                _instanceMutex = null;

                _log.LogInformation("Application shutdown cleanup completed.");
                Exit();
            });
        }
        catch (Exception ex)
        {
            _log.LogCritical(ex, "Final application shutdown on the UI thread failed.");
        }
        finally
        {
            _appLoggerFactory.Dispose();
        }
    }

    private Task RunOnDispatcherAsync(Action action)
    {
        var dispatcher = _dispatcher
            ?? throw new InvalidOperationException("The UI dispatcher is unavailable.");
        if (dispatcher.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(() =>
            {
                try
                {
                    action();
                    completion.TrySetResult();
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }))
        {
            completion.TrySetException(new InvalidOperationException(
                "Final shutdown could not be queued on the UI dispatcher."));
        }

        return completion.Task;
    }
}

internal enum AppShutdownReason
{
    TrayMenu,
    RestartManager,
    ExternalRequest,
    UpdateInstall
}
