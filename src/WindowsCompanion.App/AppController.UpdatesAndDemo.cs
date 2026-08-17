using Microsoft.Extensions.Logging;
using WindowsCompanion.Core.App;
using WindowsCompanion.Core.Lifecycle;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;
using WindowsCompanion.Core.Updates;
using WindowsCompanion_App.Services;

namespace WindowsCompanion_App;

public sealed partial class AppController
{
    /// <summary>
    /// Starts the one best-effort release lookup for this process without delaying
    /// window creation or Home Assistant connection work.
    /// </summary>
    public void StartUpdateCheck()
    {
        if (!_enableStartupUpdates) return;
        if (CurrentUpdateMode == UpdateMode.Disabled) return;
        if (Interlocked.Exchange(ref _updateCheckStarted, 1) != 0) return;
        _updateCheckTask = _startupUpdates.CheckAsync(
            UpdateCheckTrigger.Automatic,
            _updateCheckCancellation.Token);
    }

    /// <summary>Starts a fresh user-visible check, cancelling an older lookup.</summary>
    public void CheckForUpdates()
    {
        if (!_enableStartupUpdates) return;
        if (CurrentUpdateMode == UpdateMode.Disabled) return;
        if (_updateCheckCancellation.IsCancellationRequested) return;
        _updateCheckTask = _startupUpdates.CheckAsync(
            UpdateCheckTrigger.User,
            _updateCheckCancellation.Token);
    }

    /// <summary>The persisted update-check/install preference for the signed-in
    /// account, or <see cref="UpdateMode.AutoInstall"/> for a new install.</summary>
    internal UpdateMode CurrentUpdateMode =>
        (_config ?? _settings.Load())?.Updates.Mode ?? UpdateMode.AutoInstall;

    /// <summary>Persists a new update-check/install preference for the current
    /// account, if a session exists.</summary>
    public void SetUpdateMode(UpdateMode mode)
    {
        if (IsDemoMode) return;
        var config = _config ?? _settings.Load();
        if (config is null) return;

        config.Updates.Mode = mode;
        _config ??= config;
        _settings.Save(config);
    }

    public UpdateCheckState UpdateState => _startupUpdates.State;

    public event Action<UpdateCheckState>? UpdateStateChanged;

    /// <summary>The latest download/verify/install progress for the update found
    /// by <see cref="UpdateState"/>, if any.</summary>
    public UpdateInstallState InstallState => _updateInstaller.State;

    public event Action<UpdateInstallState>? InstallStateChanged;

    /// <summary>
    /// The outcome of a silent install that finished while the app was closed for
    /// it, read once at startup so the UI can show a one-time success/failure
    /// banner. Null when no install ran since the last time this was read.
    /// </summary>
    internal LastInstallResult? LastInstallResult => _lastInstallResult;

    /// <summary>
    /// Runs the verified update that reached <see cref="UpdateInstallPhase.ReadyToInstall"/>.
    /// The installer closes this process as part of installing, so this call may
    /// never return normally; failures are published through
    /// <see cref="InstallStateChanged"/> instead of throwing to the caller.
    /// </summary>
    public async Task InstallUpdateAsync()
    {
        try
        {
            await _updateInstaller.InstallAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _loggerFactory
                .CreateLogger<AppController>()
                .LogWarning(ex, "The update could not be installed silently.");
        }
    }

    private void OnUpdateStateChanged(UpdateCheckState state)
    {
        UpdateStateChanged?.Invoke(state);

        if (state.Status != UpdateCheckStatus.Available || state.AvailableUpdate is null) return;
        if (CurrentUpdateMode != UpdateMode.AutoInstall) return;

        var installState = _updateInstaller.State;
        var alreadyHandling = installState.Version.Equals(state.AvailableUpdate.AvailableVersion)
            && installState.Phase is not UpdateInstallPhase.NotStarted and not UpdateInstallPhase.Failed;
        if (alreadyHandling) return;

        _ = _updateInstaller.DownloadAsync(state.AvailableUpdate, _updateArchitecture);
    }

    private void OnUpdateInstallStateChanged(UpdateInstallState state) =>
        InstallStateChanged?.Invoke(state);

    private void NotifyUpdateAvailable(AvailableUpdate update)
    {
        // State was published before this best-effort toast. The tray badge and
        // in-app banner therefore remain available if the notification fails.
        try
        {
            _updateNotifications.Show(update);
        }
        catch (Exception ex)
        {
            _loggerFactory
                .CreateLogger<AppController>()
                .LogDebug(ex, "The Windows update notification could not be shown.");
        }
    }

    /// <summary>Whether the local, server-less demo is running.</summary>
    public bool IsDemoMode => _demo is not null;

    /// <summary>
    /// Starts the local demo: the sensor catalog becomes browsable without a Home
    /// Assistant server. Nothing is registered, saved or transmitted, and no sensor
    /// source is started, so switching a sensor on only affects the local preview.
    /// </summary>
    public void EnterDemoMode()
    {
        if (_demo is not null) return;
        if (_connection is not null || _catalog is not null)
            throw new InvalidOperationException("Demo mode is only available while disconnected.");

        var preferences = new SensorPreferences();
        var config = new ServerConfig { Sensors = preferences };
        var lifecycle = CreateLifecycleCoordinator();
        var demo = new DemoSession(
            _sensorSourceFactory(config, lifecycle, _lifecycleSignalSourceFactory()),
            preferences);
        _demo = demo;
        _catalog = demo.Catalog;
    }

    /// <summary>Ends the demo and discards everything it produced.</summary>
    public void ExitDemoMode()
    {
        var demo = _demo;
        if (demo is null) return;

        _demo = null;
        if (ReferenceEquals(_catalog, demo.Catalog)) _catalog = null;
        demo.End();
    }

    public Task<WinGetCapabilityResult> ProbeWinGetCapabilityAsync(
        CancellationToken ct = default) =>
        _winGetUpdates.ProbeCapabilityAsync(ct);

    private sealed class UpdateNotificationSink(Action<AvailableUpdate> notify)
        : IUpdateNotificationSink
    {
        public void Show(AvailableUpdate update) => notify(update);
    }

    private sealed class NoOpUpdateNotificationSink : IUpdateNotificationSink
    {
        public void Show(AvailableUpdate update)
        {
        }
    }
}
