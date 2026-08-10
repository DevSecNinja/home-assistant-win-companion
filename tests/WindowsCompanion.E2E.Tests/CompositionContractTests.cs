using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WindowsCompanion.Core.Abstractions;
using WindowsCompanion.Core.App;
using WindowsCompanion.Core.HomeAssistant;
using WindowsCompanion.Core.Lifecycle;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;
using WindowsCompanion.Testing;
using WindowsCompanion_App;
using WindowsCompanion_App.Services;

namespace WindowsCompanion.E2E.Tests;

public sealed class CompositionContractTests
{
    [Fact]
    public void Production_composition_keeps_platform_defaults()
    {
        var dependencies = AppControllerDependencies.CreateProduction();
        try
        {
            Assert.IsType<WindowsSecretStore>(dependencies.SecretStore.Value);
            Assert.Equal(
                WindowsSecretStore.DefaultResource,
                ((WindowsSecretStore)dependencies.SecretStore.Value).Resource);
            Assert.IsType<SettingsStore>(dependencies.SettingsStore.Value);
            Assert.IsType<WindowsNetworkContextProvider>(dependencies.Network.Value);
            Assert.IsType<WindowsSystemStatusProvider>(dependencies.SystemStatus.Value);
            Assert.IsType<ToastNotifier>(dependencies.NotificationSink.Value);
            Assert.IsType<ShellUriLauncher>(dependencies.UriLauncher.Value);
            using var socket = dependencies.WebSocketFactory();
            Assert.IsType<ClientWebSocketAdapter>(socket);
            Assert.NotNull(typeof(AppController).GetConstructor(Type.EmptyTypes));
        }
        finally
        {
            foreach (var dependency in dependencies.OwnedValues().Reverse())
                (dependency as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void OAuth_service_defaults_to_the_shell_launcher()
    {
        using var http = new HttpClient();
        var service = new OAuthLoginService(http);
        var launcher = typeof(OAuthLoginService)
            .GetField("_uriLauncher", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(service);

        Assert.IsType<ShellUriLauncher>(launcher);
    }

    [Fact]
    public async Task Controller_disposes_only_dependencies_declared_owned()
    {
        var ownedLauncher = new TrackingLauncher();
        var borrowedLauncher = new TrackingLauncher();

        await using (var controller = new AppController(
                         CreateDependencies(new OwnedDependency<IUriLauncher>(
                             ownedLauncher, true))))
        {
        }

        await using (var controller = new AppController(
                         CreateDependencies(new OwnedDependency<IUriLauncher>(
                             borrowedLauncher, false))))
        {
        }

        Assert.True(ownedLauncher.Disposed);
        Assert.False(borrowedLauncher.Disposed);
    }

    [Fact]
    public void Credential_resources_are_isolated_and_cleanup_is_scoped()
    {
        var first = new WindowsSecretStore($"WindowsCompanion.Tests.{Guid.NewGuid():N}");
        var second = new WindowsSecretStore($"WindowsCompanion.Tests.{Guid.NewGuid():N}");
        try
        {
            first.Save("contract-key", "first-value");
            second.Save("contract-key", "second-value");

            Assert.Equal("first-value", first.Get("contract-key"));
            Assert.Equal("second-value", second.Get("contract-key"));

            first.Clear();
            Assert.Null(first.Get("contract-key"));
            Assert.Equal("second-value", second.Get("contract-key"));
        }
        finally
        {
            first.Clear();
            second.Clear();
        }
    }

    [Fact]
    public async Task Injected_uri_launcher_completes_real_oauth_handoff()
    {
        await using var scenario = await FakeHaScenario.StartAsync("composition-oauth");
        using var browserHttp = new HttpClient();
        using var applicationHttp = new HttpClient();
        var launcher = new FollowingLauncher(browserHttp);
        var service = new OAuthLoginService(applicationHttp, launcher);

        var tokens = await service.SignInAsync(scenario.BaseUrl!.AbsoluteUri);

        Assert.NotNull(launcher.Launched);
        Assert.Equal("/auth/authorize", launcher.Launched!.AbsolutePath);
        Assert.Equal(scenario.AccessToken, tokens.AccessToken);
        Assert.Equal(scenario.RefreshToken, tokens.RefreshToken);
    }

    private static AppControllerDependencies CreateDependencies(
        OwnedDependency<IUriLauncher> launcher)
    {
        var settingsPath = Path.Combine(
            AppContext.BaseDirectory,
            "test-state",
            Guid.NewGuid().ToString("N"),
            "settings.json");

        return new AppControllerDependencies
        {
            HttpClient = new(new HttpClient(), true),
            SecretStore = new(new MemorySecretStore()),
            SettingsStore = new(new SettingsStore(settingsPath)),
            SystemStatus = new(new FixedSystemStatus()),
            NotificationSink = new(new NoOpNotificationSink()),
            WinGetUpdates = new(new NoOpWinGetProvider()),
            UriLauncher = launcher,
            Network = new(new FixedNetwork()),
            LoggerFactory = new(NullLoggerFactory.Instance),
            WebSocketFactory = static () => throw new InvalidOperationException("Not used."),
            SensorSourceFactory = static (_, _, _) => [],
            LifecycleJournalFactory = static () => new MemoryLifecycleJournal(),
            LifecycleSignalSourceFactory = static () => new NoOpLifecycleSignals()
        };
    }

    private sealed class FollowingLauncher(HttpClient http) : IUriLauncher
    {
        public Uri? Launched { get; private set; }

        public async Task LaunchAsync(
            Uri uri,
            CancellationToken cancellationToken = default)
        {
            Launched = uri;
            using var response = await http.GetAsync(uri, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }

    private sealed class TrackingLauncher : IUriLauncher, IDisposable
    {
        public bool Disposed { get; private set; }
        public Task LaunchAsync(Uri uri, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public void Dispose() => Disposed = true;
    }

    private sealed class MemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _values = [];
        public void Save(string key, string value) => _values[key] = value;
        public string? Get(string key) => _values.GetValueOrDefault(key);
        public void Delete(string key) => _values.Remove(key);
    }

    private sealed class FixedSystemStatus : ISystemStatusProvider
    {
        public SystemStatus GetStatus() => new(false, 100, PowerState.PluggedIn);
    }

    private sealed class NoOpNotificationSink : INotificationSink
    {
        public void Show(NotificationMessage notification)
        {
        }
    }

    private sealed class NoOpWinGetProvider : IWinGetUpdateProvider
    {
        public Task<WinGetCapabilityResult> ProbeCapabilityAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(WinGetCapabilityResult.FromStatus(
                WinGetCapabilityStatus.ModuleMissing));

        public Task<WinGetUpdateResult> CheckForUpdatesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WinGetUpdateResult(WinGetUpdateStatus.Ready, []));
    }

    private sealed class FixedNetwork : INetworkContextProvider
    {
        public NetworkContext GetCurrent() => NetworkContext.Offline;
        public event Action? NetworkChanged
        {
            add { }
            remove { }
        }
        public void Start()
        {
        }
        public void Stop()
        {
        }
    }

    private sealed class MemoryLifecycleJournal : ILifecycleJournal
    {
        public LifecycleRecord? Read() => null;
        public void Write(LifecycleRecord record)
        {
        }
    }

    private sealed class NoOpLifecycleSignals : ILifecycleSignalSource
    {
        public event Action<LifecycleSignal>? SignalObserved
        {
            add { }
            remove { }
        }
        public void Start()
        {
        }
        public void Stop()
        {
        }
    }
}
