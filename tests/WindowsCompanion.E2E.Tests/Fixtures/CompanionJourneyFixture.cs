using System.Runtime.ExceptionServices;
using WindowsCompanion.Core.Abstractions;
using WindowsCompanion.Core.App;
using WindowsCompanion.Core.HomeAssistant;
using WindowsCompanion.Core.Lifecycle;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;
using WindowsCompanion.Testing;
using WindowsCompanion_App;
using WindowsCompanion_App.Services;

namespace WindowsCompanion.E2E.Tests.Fixtures;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CompanionJourneyCollection
{
    public const string Name = "Companion journeys";
}

internal sealed class CompanionJourneyFixture : IAsyncDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private AppController? _controller;
    private bool _disposed;

    private CompanionJourneyFixture(
        FakeHaScenario scenario,
        string profileDirectory,
        WindowsSecretStore secretStore,
        TestUriLauncher uriLauncher,
        FailureEvidence evidence)
    {
        Scenario = scenario;
        ProfileDirectory = profileDirectory;
        SettingsPath = Path.Combine(profileDirectory, "settings.json");
        SecretStore = secretStore;
        UriLauncher = uriLauncher;
        Evidence = evidence;
    }

    public FakeHaScenario Scenario { get; }
    public string ProfileDirectory { get; }
    public string SettingsPath { get; }
    public WindowsSecretStore SecretStore { get; }
    public TestUriLauncher UriLauncher { get; }
    public FailureEvidence Evidence { get; }
    public DeterministicSensorSource Sensors { get; } = new();
    public DeterministicNotificationSink Notifications { get; } = new();
    public DeterministicNetworkContext Network { get; } = new();
    public AppController Controller =>
        _controller ?? throw new InvalidOperationException("No controller has been created.");

    public static async Task<CompanionJourneyFixture> StartAsync(
        string scenarioId,
        string? evidenceRoot = null)
    {
        var profileDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "journey-state",
            $"{scenarioId}-{Guid.NewGuid():N}");
        var resource = $"WindowsCompanion.E2E.{Guid.NewGuid():N}";
        WindowsSecretStore? secretStore = null;
        FakeHaScenario? scenario = null;
        TestUriLauncher? uriLauncher = null;
        FailureEvidence? evidence = null;

        try
        {
            Directory.CreateDirectory(profileDirectory);
            secretStore = new WindowsSecretStore(resource);
            secretStore.Clear();
            scenario = await FakeHaScenario.StartAsync(scenarioId).ConfigureAwait(false);
            uriLauncher = new TestUriLauncher();
            evidence = new FailureEvidence(scenario, profileDirectory, evidenceRoot);
            return new CompanionJourneyFixture(
                scenario,
                profileDirectory,
                secretStore,
                uriLauncher,
                evidence);
        }
        catch (Exception startupFailure)
        {
            try
            {
                evidence ??= new FailureEvidence(
                    scenarioId,
                    profileDirectory,
                    evidenceRoot);
                await evidence.CaptureAsync(
                        "start companion journey",
                        ConnectionState.Disconnected,
                        startupFailure)
                    .ConfigureAwait(false);
            }
            catch (Exception evidenceFailure)
            {
                startupFailure.Data["FailureEvidenceCaptureError"] =
                    evidenceFailure.ToString();
            }

            try
            {
                uriLauncher?.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                AddSecondaryFailure(startupFailure, cleanupFailure);
            }

            if (scenario is not null)
            {
                try
                {
                    await scenario.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupFailure)
                {
                    AddSecondaryFailure(startupFailure, cleanupFailure);
                }
            }

            try
            {
                secretStore?.Clear();
            }
            catch (Exception cleanupFailure)
            {
                AddSecondaryFailure(startupFailure, cleanupFailure);
            }

            try
            {
                evidence?.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                AddSecondaryFailure(startupFailure, cleanupFailure);
            }

            try
            {
                DeleteProfile(profileDirectory);
            }
            catch (Exception cleanupFailure)
            {
                AddSecondaryFailure(startupFailure, cleanupFailure);
            }

            throw;
        }
    }

    public static async Task RunAsync(
        string scenarioId,
        string failingStep,
        Func<CompanionJourneyFixture, Task> journey,
        string? evidenceRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioId);
        ArgumentException.ThrowIfNullOrWhiteSpace(failingStep);
        ArgumentNullException.ThrowIfNull(journey);

        var fixture = await StartAsync(scenarioId, evidenceRoot).ConfigureAwait(false);
        ExceptionDispatchInfo? originalFailure = null;
        try
        {
            await journey(fixture).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            try
            {
                await fixture.Evidence.CaptureAsync(
                        failingStep,
                        fixture._controller?.State ?? ConnectionState.Disconnected,
                        exception)
                    .ConfigureAwait(false);
            }
            catch (Exception evidenceFailure)
            {
                exception.Data["FailureEvidenceCaptureError"] = evidenceFailure.ToString();
            }

            originalFailure = ExceptionDispatchInfo.Capture(exception);
        }

        Exception? cleanupFailure = null;
        try
        {
            await fixture.DisposeAsync(captureFailureEvidence: originalFailure is null)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            cleanupFailure = exception;
        }

        if (originalFailure is not null)
        {
            if (cleanupFailure is not null)
                originalFailure.SourceException.Data["FixtureCleanupError"] =
                    cleanupFailure.ToString();
            originalFailure.Throw();
        }

        if (cleanupFailure is not null)
            ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
    }

    public AppController CreateController()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_controller is not null)
            throw new InvalidOperationException("Dispose or restart the active controller first.");

        var dependencies = new AppControllerDependencies
        {
            HttpClient = new(new HttpClient(), true),
            SecretStore = new(SecretStore),
            SettingsStore = new(new SettingsStore(SettingsPath)),
            SystemStatus = new(new DeterministicSystemStatus()),
            NotificationSink = new(Notifications),
            WinGetUpdates = new(new DeterministicWinGetUpdates()),
            UriLauncher = new(UriLauncher),
            Network = new(Network),
            LoggerFactory = new(Evidence.LoggerFactory),
            WebSocketFactory = static () => new ClientWebSocketAdapter(),
            SensorSourceFactory = (_, _, _) => [Sensors],
            LifecycleJournalFactory = static () => new MemoryLifecycleJournal(),
            LifecycleSignalSourceFactory = static () => new NoOpLifecycleSignalSource()
        };

        _controller = new AppController(dependencies);
        return _controller;
    }

    public async Task<AppController> SignInAsync(bool waitForReady = true)
    {
        var controller = CreateController();
        await controller.SignInAsync(Scenario.BaseUrl!.AbsoluteUri).ConfigureAwait(false);
        if (waitForReady) await WaitForReadyAsync().ConfigureAwait(false);
        return controller;
    }

    public async Task<AppController> ResumePreauthorizedAsync(bool waitForReady = true)
    {
        var config = new ServerConfig
        {
            BaseUrl = Scenario.BaseUrl!.AbsoluteUri,
            DeviceId = $"e2e-device-{Guid.NewGuid():N}"
        };
        config.SetSingleUrl(config.BaseUrl);
        new SessionStore(new SettingsStore(SettingsPath), SecretStore).Save(config);
        SecretStore.Save(AppConstants.RefreshTokenKey, Scenario.RefreshToken);

        var controller = CreateController();
        Assert.True(await controller.TryResumeAsync().ConfigureAwait(false));
        if (waitForReady) await WaitForReadyAsync().ConfigureAwait(false);
        return controller;
    }

    public async Task<AppController> RestartAsync()
    {
        var boundary = LastInteractionSequence;
        await DisposeControllerAsync().ConfigureAwait(false);
        var controller = CreateController();
        Assert.True(await controller.TryResumeAsync().ConfigureAwait(false));
        await WaitForReadyAsync(boundary).ConfigureAwait(false);
        return controller;
    }

    public async Task ReconnectAsync()
    {
        var boundary = LastInteractionSequence;
        await Controller.ReconnectAsync().ConfigureAwait(false);
        await WaitForReadyAsync(boundary).ConfigureAwait(false);
    }

    public ServerConfig LoadConfig() =>
        new SessionStore(new SettingsStore(SettingsPath), SecretStore).Load()
        ?? throw new InvalidOperationException("The fixture has no saved session.");

    public long LastInteractionSequence =>
        Scenario.Interactions.Snapshot().LastOrDefault()?.Sequence ?? 0;

    public async Task WaitForReadyAsync(long afterSequence = 0)
    {
        await Scenario.Interactions.WaitForAsync(
            interaction => interaction.PathOrMessageType == "update_sensor_states"
                           && interaction.Outcome == "Success",
            DefaultTimeout,
            afterSequence: afterSequence).ConfigureAwait(false);
        await Scenario.Interactions.WaitForAsync(
            interaction => interaction.PathOrMessageType == "push_subscribed",
            DefaultTimeout,
            afterSequence: afterSequence).ConfigureAwait(false);
        await WaitForStateAsync(ConnectionState.Connected).ConfigureAwait(false);
    }

    public async Task WaitForStateAsync(ConnectionState expected)
    {
        if (Controller.State == expected) return;

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStateChanged(ConnectionState state)
        {
            if (state == expected) completion.TrySetResult();
        }

        Controller.StateChanged += OnStateChanged;
        try
        {
            if (Controller.State == expected) return;
            await completion.Task.WaitAsync(DefaultTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException(
                $"Timed out waiting for controller state {expected}; current state is "
                + $"{Controller.State}.{Environment.NewLine}{Scenario.Interactions.FormatHistory()}",
                exception);
        }
        finally
        {
            Controller.StateChanged -= OnStateChanged;
        }
    }

    public async Task DisposeControllerAsync()
    {
        if (_controller is null) return;
        var controller = _controller;
        _controller = null;
        await controller.DisposeAsync().ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => DisposeAsync(captureFailureEvidence: true);

    private async ValueTask DisposeAsync(bool captureFailureEvidence)
    {
        if (_disposed) return;
        _disposed = true;
        var companionState = _controller?.State ?? ConnectionState.Disconnected;
        ExceptionDispatchInfo? failure = null;
        var evidenceCaptured = false;

        void Record(Exception exception)
        {
            if (failure is null)
                failure = ExceptionDispatchInfo.Capture(exception);
            else
                AddSecondaryFailure(failure.SourceException, exception);
        }

        async Task CaptureFirstFailureAsync()
        {
            if (!captureFailureEvidence || evidenceCaptured || failure is null) return;
            evidenceCaptured = true;
            await CaptureCleanupFailureAsync(
                    failure.SourceException,
                    companionState)
                .ConfigureAwait(false);
        }

        try
        {
            await DisposeControllerAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Record(exception);
            await CaptureFirstFailureAsync().ConfigureAwait(false);
        }

        try
        {
            await Scenario.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Record(exception);
            await CaptureFirstFailureAsync().ConfigureAwait(false);
        }

        try
        {
            SecretStore.Clear();
        }
        catch (Exception exception)
        {
            Record(exception);
            await CaptureFirstFailureAsync().ConfigureAwait(false);
        }

        try
        {
            UriLauncher.Dispose();
        }
        catch (Exception exception)
        {
            Record(exception);
            await CaptureFirstFailureAsync().ConfigureAwait(false);
        }

        try
        {
            VerifyNonDestructiveCleanup();
        }
        catch (Exception exception)
        {
            Record(exception);
            await CaptureFirstFailureAsync().ConfigureAwait(false);
        }

        try
        {
            DeleteProfile(ProfileDirectory);
        }
        catch (Exception exception)
        {
            Record(exception);
            await CaptureFirstFailureAsync().ConfigureAwait(false);
        }

        try
        {
            Evidence.Dispose();
        }
        catch (Exception exception)
        {
            Record(exception);
            await CaptureFirstFailureAsync().ConfigureAwait(false);
        }

        if (Directory.Exists(ProfileDirectory))
        {
            try
            {
                DeleteProfile(ProfileDirectory);
            }
            catch (Exception exception)
            {
                Record(exception);
            }
        }

        failure?.Throw();
    }

    private static void DeleteProfile(string path)
    {
        if (!Directory.Exists(path)) return;
        Directory.Delete(path, recursive: true);
    }

    private void VerifyNonDestructiveCleanup()
    {
        var failures = new List<string>();
        if (Scenario.Lifecycle != FakeHaScenarioLifecycle.Disposed)
            failures.Add($"scenario lifecycle is {Scenario.Lifecycle}");
        if (SecretStore.Get(AppConstants.RefreshTokenKey) is not null
            || SecretStore.Get(SessionStore.WebhookIdKey) is not null
            || SecretStore.Get(SessionStore.CloudhookUrlKey) is not null)
        {
            failures.Add("Credential Locker entries remain");
        }
        if (Sensors.IsRunning || Sensors.StartCount != Sensors.StopCount)
            failures.Add("sensor source lifetime is unbalanced");
        if (Network.StartCount != Network.StopCount)
            failures.Add("network source lifetime is unbalanced");

        if (failures.Count > 0)
            throw new InvalidOperationException(
                "Companion journey cleanup failed: " + string.Join("; ", failures));
    }

    private async Task CaptureCleanupFailureAsync(
        Exception cleanupFailure,
        ConnectionState companionState)
    {
        try
        {
            await Evidence.CaptureAsync(
                    "clean up companion journey",
                    companionState,
                    cleanupFailure)
                .ConfigureAwait(false);
        }
        catch (Exception evidenceFailure)
        {
            cleanupFailure.Data["FailureEvidenceCaptureError"] =
                evidenceFailure.ToString();
        }
    }

    private static void AddSecondaryFailure(
        Exception original,
        Exception secondary)
    {
        var index = 1;
        string key;
        do
        {
            key = $"CleanupError{index++}";
        } while (original.Data.Contains(key));
        original.Data[key] = secondary.ToString();
    }

    internal sealed class DeterministicSensorSource : ISensorSource
    {
        public const string EnabledId = "e2e_enabled";
        public const string OptionalId = "e2e_optional";

        private readonly object _gate = new();
        private readonly Dictionary<string, object> _states = new(StringComparer.Ordinal)
        {
            [EnabledId] = "synthetic-ready",
            [OptionalId] = 7
        };
        private Action? _onChanged;

        public IReadOnlyList<SensorDefinition> Definitions { get; } =
        [
            new(
                EnabledId,
                "E2E enabled",
                "Deterministic enabled test sensor.",
                SensorPrivacy.Benign,
                EnabledByDefault: true),
            new(
                OptionalId,
                "E2E optional",
                "Deterministic opt-in test sensor.",
                SensorPrivacy.Sensitive,
                EnabledByDefault: false)
        ];

        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public int ReadCount { get; private set; }
        public bool IsRunning { get; private set; }

        public IReadOnlyList<Sensor> Read(
            IReadOnlySet<string> enabled,
            SensorReadContext context)
        {
            lock (_gate)
            {
                ReadCount++;
                return Definitions
                    .Where(definition => enabled.Contains(definition.UniqueId))
                    .Select(definition => new Sensor
                    {
                        UniqueId = definition.UniqueId,
                        Name = definition.Name,
                        Type = "sensor",
                        State = _states[definition.UniqueId],
                        Attributes = new Dictionary<string, object>
                        {
                            ["source"] = "deterministic",
                            ["reason"] = context.Reason
                        }
                    })
                    .ToArray();
            }
        }

        public void SetState(string uniqueId, object state, bool notify = false)
        {
            Action? onChanged;
            lock (_gate)
            {
                if (!_states.ContainsKey(uniqueId))
                    throw new ArgumentOutOfRangeException(nameof(uniqueId));
                _states[uniqueId] = state;
                onChanged = notify && IsRunning ? _onChanged : null;
            }

            onChanged?.Invoke();
        }

        public void Start(Action onChanged)
        {
            lock (_gate)
            {
                if (IsRunning) return;
                IsRunning = true;
                StartCount++;
                _onChanged = onChanged;
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                if (!IsRunning) return;
                IsRunning = false;
                StopCount++;
                _onChanged = null;
            }
        }
    }

    internal sealed class DeterministicNotificationSink : INotificationSink
    {
        private readonly object _gate = new();
        private readonly List<NotificationMessage> _messages = [];
        private readonly List<Waiter> _waiters = [];

        public IReadOnlyList<NotificationMessage> Snapshot()
        {
            lock (_gate) return _messages.ToArray();
        }

        public void Show(NotificationMessage notification)
        {
            List<Waiter> completed;
            lock (_gate)
            {
                _messages.Add(notification);
                completed = _waiters
                    .Where(waiter => waiter.Predicate(notification))
                    .ToList();
                foreach (var waiter in completed) _waiters.Remove(waiter);
            }

            foreach (var waiter in completed)
                waiter.Completion.TrySetResult(notification);
        }

        public async Task<NotificationMessage> WaitForAsync(
            Func<NotificationMessage, bool> predicate,
            TimeSpan? timeout = null)
        {
            Waiter waiter;
            lock (_gate)
            {
                var existing = _messages.FirstOrDefault(predicate);
                if (existing is not null) return existing;
                waiter = new Waiter(predicate);
                _waiters.Add(waiter);
            }

            try
            {
                return await waiter.Completion.Task
                    .WaitAsync(timeout ?? DefaultTimeout)
                    .ConfigureAwait(false);
            }
            finally
            {
                lock (_gate) _waiters.Remove(waiter);
            }
        }

        private sealed class Waiter(Func<NotificationMessage, bool> predicate)
        {
            public Func<NotificationMessage, bool> Predicate { get; } = predicate;
            public TaskCompletionSource<NotificationMessage> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    internal sealed class DeterministicNetworkContext : INetworkContextProvider
    {
        public NetworkContext Current { get; private set; } = NetworkContext.Offline;
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public event Action? NetworkChanged;

        public NetworkContext GetCurrent() => Current;

        public void Set(NetworkContext current)
        {
            Current = current;
            NetworkChanged?.Invoke();
        }

        public void Start() => StartCount++;
        public void Stop() => StopCount++;
    }

    private sealed class DeterministicSystemStatus : ISystemStatusProvider
    {
        public SystemStatus GetStatus() => new(false, 100, PowerState.PluggedIn);
    }

    private sealed class DeterministicWinGetUpdates : IWinGetUpdateProvider
    {
        public Task<bool> IsModuleInstalledAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<WinGetUpdateResult> CheckForUpdatesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WinGetUpdateResult(WinGetUpdateStatus.Ready, []));
    }

    private sealed class MemoryLifecycleJournal : ILifecycleJournal
    {
        private LifecycleRecord? _record;
        public LifecycleRecord? Read() => _record;
        public void Write(LifecycleRecord record) => _record = record;
    }

    private sealed class NoOpLifecycleSignalSource : ILifecycleSignalSource
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
