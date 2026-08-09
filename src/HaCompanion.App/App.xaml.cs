using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using HaCompanion.Core.App;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace HaCompanion_App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    /// <summary>Shared coordinator for the OAuth session and Home Assistant connection.</summary>
    public static AppController Controller { get; } = new();
    
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
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

        var startupLaunch = StartupCommand.IsStartupLaunch(Environment.GetCommandLineArgs());
        var startHidden = startupLaunch && Controller.HasSavedSession;
        _window = new MainWindow(startHidden);
        _window.Activate();
    }
}
