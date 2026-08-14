using Microsoft.Extensions.Logging;
using WindowsCompanion.Core.App;
using WindowsCompanion.Core.HomeAssistant;
using WindowsCompanion.Core.Lifecycle;
using WindowsCompanion_App.Services;

namespace WindowsCompanion_App;

internal static class ProductionAppComposition
{
    public static AppControllerDependencies CreateDependencies()
    {
        var loggerFactory = CreateLoggerFactory();
        var status = new WindowsSystemStatusProvider();
        var notifications = new ToastNotifier();
        var winGet = new PowerShellWinGetUpdateProvider(
            loggerFactory.CreateLogger<PowerShellWinGetUpdateProvider>());

        return new AppControllerDependencies
        {
            HttpClient = new(new HttpClient(), true),
            SecretStore = new(new WindowsSecretStore(), true),
            SettingsStore = new(new SettingsStore(), true),
            SystemStatus = new(status, true),
            NotificationSink = new(notifications, true),
            WinGetUpdates = new(winGet, true),
            UriLauncher = new(new ShellUriLauncher(), true),
            Network = new(new WindowsNetworkContextProvider(), true),
            LoggerFactory = new(loggerFactory, true),
            UpdateHttpClient = new(GitHubReleaseClient.CreateHttpClient(), true),
            UpdateNotificationSink = new(notifications),
            EnableStartupUpdates = true,
            WebSocketFactory = static () => new ClientWebSocketAdapter(),
            LifecycleJournalFactory = static () => new FileLifecycleJournal(),
            LifecycleSignalSourceFactory = static () => new WindowsLifecycleSignalSource(),
            SensorSourceFactory = (config, lifecycle, lifecycleSignals) =>
                ProductionSensorComposition.CreateSources(
                    config,
                    lifecycle,
                    lifecycleSignals,
                    status,
                    winGet)
        };
    }

    private static ILoggerFactory CreateLoggerFactory() =>
        Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new FileLoggerProvider(LogLevel.Debug));
            builder.SetMinimumLevel(LogLevel.Debug);
        });
}
