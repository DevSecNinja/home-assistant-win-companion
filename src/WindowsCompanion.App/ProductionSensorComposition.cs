using WindowsCompanion.Core.Abstractions;
using WindowsCompanion.Core.Lifecycle;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;
using WindowsCompanion.Core.Updates;
using WindowsCompanion_App.Services;

namespace WindowsCompanion_App;

internal static class ProductionSensorComposition
{
    public static IReadOnlyList<ISensorSource> CreateSources(
        ServerConfig config,
        LifecycleCoordinator lifecycle,
        ILifecycleSignalSource lifecycleSignals,
        ISystemStatusProvider status,
        IWinGetUpdateProvider winGet,
        ILocationProvider location) =>
    [
        new BatterySensorSource(status),
        new ActiveSensorSource(config.Sensors),
        new NetworkSensorSource(config.Sensors),
        new WifiSensorSource(config.Sensors),
        new SystemSensorSource(),
        new DomainSensorSource(config.Sensors),
        new DisplaySensorSource(config.Sensors),
        new WindowsThemeSensorSource(),
        new LocaleSensorSource(),
        new DiskUsageSensorSource(),
        new NotificationStateSensorSource(),
        new CapabilityUsageSensorSource(config.Sensors),
        new AudioDeviceSensorSource(config.Sensors),
        new FrontmostAppSensorSource(config.Sensors),
        new WinGetUpdateSensorSource(winGet, config.Sensors),
        new LocationSensorSource(location, config.Sensors),
        new LifecycleSensorSource(lifecycle, lifecycleSignals)
    ];
}
