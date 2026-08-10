using Microsoft.Extensions.Logging;
using WindowsCompanion.Core.Abstractions;
using WindowsCompanion.Core.App;
using WindowsCompanion.Core.HomeAssistant;
using WindowsCompanion.Core.Lifecycle;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;
using WindowsCompanion_App.Services;

namespace WindowsCompanion_App;

internal sealed record OwnedDependency<T>(T Value, bool IsOwned = false)
    where T : class;

/// <summary>Receives notifications accepted by the connection manager.</summary>
public interface INotificationSink
{
    void Show(NotificationMessage notification);
}

internal sealed class AppControllerDependencies
{
    public required OwnedDependency<HttpClient> HttpClient { get; init; }
    public required OwnedDependency<ISecretStore> SecretStore { get; init; }
    public required OwnedDependency<SettingsStore> SettingsStore { get; init; }
    public required OwnedDependency<ISystemStatusProvider> SystemStatus { get; init; }
    public required OwnedDependency<INotificationSink> NotificationSink { get; init; }
    public required OwnedDependency<IWinGetUpdateProvider> WinGetUpdates { get; init; }
    public required OwnedDependency<IUriLauncher> UriLauncher { get; init; }
    public required OwnedDependency<INetworkContextProvider> Network { get; init; }
    public required OwnedDependency<ILoggerFactory> LoggerFactory { get; init; }
    public required Func<IHaSocket> WebSocketFactory { get; init; }
    public required Func<ServerConfig, LifecycleCoordinator, ILifecycleSignalSource, IReadOnlyList<ISensorSource>>
        SensorSourceFactory { get; init; }
    public required Func<ILifecycleJournal> LifecycleJournalFactory { get; init; }
    public required Func<ILifecycleSignalSource> LifecycleSignalSourceFactory { get; init; }

    public static AppControllerDependencies CreateProduction()
    {
        var status = new WindowsSystemStatusProvider();
        var winGet = new PowerShellWinGetUpdateProvider();

        return new AppControllerDependencies
        {
            HttpClient = new(new HttpClient(), true),
            SecretStore = new(new WindowsSecretStore(), true),
            SettingsStore = new(new SettingsStore(), true),
            SystemStatus = new(status, true),
            NotificationSink = new(new ToastNotifier(), true),
            WinGetUpdates = new(winGet, true),
            UriLauncher = new(new ShellUriLauncher(), true),
            Network = new(new WindowsNetworkContextProvider(), true),
            LoggerFactory = new(Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
            {
                builder.AddProvider(new FileLoggerProvider(LogLevel.Debug));
                builder.SetMinimumLevel(LogLevel.Debug);
            }), true),
            WebSocketFactory = static () => new ClientWebSocketAdapter(),
            LifecycleJournalFactory = static () => new FileLifecycleJournal(),
            LifecycleSignalSourceFactory = static () => new WindowsLifecycleSignalSource(),
            SensorSourceFactory = (config, lifecycle, lifecycleSignals) =>
            [
                new BatterySensorSource(status),
                new ActiveSensorSource(config.Sensors),
                new NetworkSensorSource(config.Sensors),
                new WifiSensorSource(config.Sensors),
                new SystemSensorSource(),
                new DomainSensorSource(config.Sensors),
                new DisplaySensorSource(),
                new WindowsThemeSensorSource(),
                new LocaleSensorSource(),
                new DiskUsageSensorSource(),
                new NotificationStateSensorSource(),
                new CapabilityUsageSensorSource(config.Sensors),
                new AudioDeviceSensorSource(config.Sensors),
                new FrontmostAppSensorSource(config.Sensors),
                new WinGetUpdateSensorSource(winGet, config.Sensors),
                new LifecycleSensorSource(lifecycle, lifecycleSignals)
            ]
        };
    }

    public IEnumerable<object> OwnedValues()
    {
        var dependencies = new object?[]
        {
            HttpClient.IsOwned ? HttpClient.Value : null,
            SecretStore.IsOwned ? SecretStore.Value : null,
            SettingsStore.IsOwned ? SettingsStore.Value : null,
            SystemStatus.IsOwned ? SystemStatus.Value : null,
            NotificationSink.IsOwned ? NotificationSink.Value : null,
            WinGetUpdates.IsOwned ? WinGetUpdates.Value : null,
            UriLauncher.IsOwned ? UriLauncher.Value : null,
            Network.IsOwned ? Network.Value : null,
            LoggerFactory.IsOwned ? LoggerFactory.Value : null
        };

        return dependencies
            .Where(static dependency => dependency is not null)
            .Cast<object>()
            .Distinct(ReferenceEqualityComparer.Instance);
    }
}
