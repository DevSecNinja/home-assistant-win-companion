using Microsoft.Extensions.Logging;
using WindowsCompanion.Core.Abstractions;
using WindowsCompanion.Core.App;
using WindowsCompanion.Core.HomeAssistant;
using WindowsCompanion.Core.Lifecycle;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;
using WindowsCompanion.Core.Updates;
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
    public OwnedDependency<HttpClient>? UpdateHttpClient { get; init; }
    public OwnedDependency<IUpdateNotificationSink>? UpdateNotificationSink { get; init; }
    public bool EnableStartupUpdates { get; init; }
    public required Func<IHaSocket> WebSocketFactory { get; init; }
    public required Func<ServerConfig, LifecycleCoordinator, ILifecycleSignalSource, IReadOnlyList<ISensorSource>>
        SensorSourceFactory { get; init; }
    public required Func<ILifecycleJournal> LifecycleJournalFactory { get; init; }
    public required Func<ILifecycleSignalSource> LifecycleSignalSourceFactory { get; init; }

    public static AppControllerDependencies CreateProduction() =>
        ProductionAppComposition.CreateDependencies();

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
            LoggerFactory.IsOwned ? LoggerFactory.Value : null,
            UpdateHttpClient is { IsOwned: true } ? UpdateHttpClient.Value : null,
            UpdateNotificationSink is { IsOwned: true } ? UpdateNotificationSink.Value : null
        };

        return dependencies
            .Where(static dependency => dependency is not null)
            .Cast<object>()
            .Distinct(ReferenceEqualityComparer.Instance);
    }
}
