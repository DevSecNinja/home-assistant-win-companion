#if DEBUG
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using WindowsCompanion.Core.Abstractions;
using WindowsCompanion.Core.App;
using WindowsCompanion.Core.HomeAssistant;
using WindowsCompanion.Core.Lifecycle;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;
using WindowsCompanion_App.Services;

namespace WindowsCompanion_App;

internal static class TestAppComposition
{
    internal static AppController Create(TestAppLaunchOptions options)
    {
        var status = new FixedSystemStatusProvider();
        var launcher = options.AutoAuthorize
            ? (IUriLauncher)new LoopbackFollowingUriLauncher(options.ServerUrl)
            : new LoopbackShellUriLauncher(options.ServerUrl);
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new TestProfileLoggerProvider(options.SettingsDirectory));
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        return new AppController(new AppControllerDependencies
        {
            HttpClient = new(new HttpClient(new TestServerOnlyHandler(options.ServerUrl)), true),
            SecretStore = new(new WindowsSecretStore(options.CredentialResource), true),
            SettingsStore = new(
                new SettingsStore(Path.Combine(options.SettingsDirectory, "settings.json")),
                true),
            SystemStatus = new(status, true),
            NotificationSink = new(new ToastNotifier(), true),
            WinGetUpdates = new(new NoOpWinGetUpdateProvider(), true),
            UriLauncher = new(launcher, true),
            Network = new(new OfflineNetworkContextProvider(), true),
            LoggerFactory = new(loggerFactory, true),
            WebSocketFactory = static () => new ClientWebSocketAdapter(),
            SensorSourceFactory = (_, _, _) => [new BatterySensorSource(status)],
            LifecycleJournalFactory = () => new FileLifecycleJournal(
                Path.Combine(options.SettingsDirectory, "lifecycle.json")),
            LifecycleSignalSourceFactory = static () => new NoOpLifecycleSignalSource()
        });
    }

    private static bool SameOrigin(Uri left, Uri right) =>
        string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.IdnHost, right.IdnHost, StringComparison.OrdinalIgnoreCase)
        && left.Port == right.Port;

    private sealed class TestServerOnlyHandler : HttpClientHandler
    {
        private readonly Uri _serverUrl;

        public TestServerOnlyHandler(Uri serverUrl)
        {
            _serverUrl = serverUrl;
            AllowAutoRedirect = false;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri is not { } uri
                || uri.Scheme != Uri.UriSchemeHttp
                || !uri.IsLoopback
                || !SameOrigin(uri, _serverUrl))
            {
                throw new HttpRequestException(
                    "Test-profile HTTP requests are restricted to the configured loopback server.");
            }

            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class LoopbackFollowingUriLauncher : IUriLauncher, IDisposable
    {
        private readonly Uri _serverUrl;
        private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false });
        private readonly HttpClient _callbackHttp = CreateCallbackClient();

        public LoopbackFollowingUriLauncher(Uri serverUrl) => _serverUrl = serverUrl;

        public async Task LaunchAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            if (!SameOrigin(uri, _serverUrl))
                throw new InvalidOperationException(
                    "Automatic test authorization must start at the configured loopback server.");

            for (var redirects = 0; redirects < 8; redirects++)
            {
                if (uri.Scheme != Uri.UriSchemeHttp || !uri.IsLoopback)
                    throw new InvalidOperationException(
                        "Automatic test authorization may follow only HTTP loopback targets.");

                using var response = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode is not (HttpStatusCode.Moved or HttpStatusCode.Redirect
                    or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect
                    or HttpStatusCode.PermanentRedirect))
                {
                    response.EnsureSuccessStatusCode();
                    return;
                }

                var location = response.Headers.Location
                               ?? throw new HttpRequestException(
                                   "The authorization redirect did not include a target.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                    uri = new UriBuilder(uri) { Host = IPAddress.Loopback.ToString() }.Uri;
                if (uri.Port == AppConstants.LoopbackPort && uri.IsLoopback)
                {
                    using var callback = await _callbackHttp
                        .GetAsync(uri, cancellationToken)
                        .ConfigureAwait(false);
                    callback.EnsureSuccessStatusCode();
                    return;
                }
            }

            throw new HttpRequestException("The authorization flow exceeded its redirect limit.");
        }

        private static HttpClient CreateCallbackClient()
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectCallback = async (context, cancellationToken) =>
                {
                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
                    {
                        LingerState = new LingerOption(enable: true, seconds: 0)
                    };
                    try
                    {
                        await socket.ConnectAsync(context.DnsEndPoint, cancellationToken)
                            .ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            };
            return new HttpClient(handler, disposeHandler: true);
        }

        public void Dispose()
        {
            _http.Dispose();
            _callbackHttp.Dispose();
        }
    }

    private sealed class LoopbackShellUriLauncher(Uri serverUrl) : IUriLauncher
    {
        private readonly ShellUriLauncher _launcher = new();

        public Task LaunchAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            if (uri.Scheme != Uri.UriSchemeHttp || !uri.IsLoopback || !SameOrigin(uri, serverUrl))
                throw new InvalidOperationException(
                    "Test authorization may open only the configured loopback server.");
            return _launcher.LaunchAsync(uri, cancellationToken);
        }
    }

    private sealed class FixedSystemStatusProvider : ISystemStatusProvider
    {
        public SystemStatus GetStatus() => new(true, 87, PowerState.Discharging);
    }

    private sealed class NoOpWinGetUpdateProvider : IWinGetUpdateProvider
    {
        public Task<bool> IsModuleInstalledAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<WinGetUpdateResult> CheckForUpdatesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WinGetUpdateResult(WinGetUpdateStatus.Ready, []));
    }

    private sealed class OfflineNetworkContextProvider : INetworkContextProvider
    {
        public NetworkContext GetCurrent() => NetworkContext.Offline;
        public event Action? NetworkChanged
        {
            add { }
            remove { }
        }
        public void Start() { }
        public void Stop() { }
    }

    private sealed class NoOpLifecycleSignalSource : ILifecycleSignalSource
    {
        public event Action<LifecycleSignal>? SignalObserved
        {
            add { }
            remove { }
        }
        public void Start() { }
        public void Stop() { }
    }
}
#endif
