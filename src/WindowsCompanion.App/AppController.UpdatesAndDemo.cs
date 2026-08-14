using Microsoft.Extensions.Logging;
using WindowsCompanion.Core.App;
using WindowsCompanion.Core.Lifecycle;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;
using WindowsCompanion.Core.Updates;

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
        if (Interlocked.Exchange(ref _updateCheckStarted, 1) != 0) return;
        _updateCheckTask = _startupUpdates.CheckAsync(
            UpdateCheckTrigger.Automatic,
            _updateCheckCancellation.Token);
    }

    /// <summary>Starts a fresh user-visible check, cancelling an older lookup.</summary>
    public void CheckForUpdates()
    {
        if (!_enableStartupUpdates) return;
        if (_updateCheckCancellation.IsCancellationRequested) return;
        _updateCheckTask = _startupUpdates.CheckAsync(
            UpdateCheckTrigger.User,
            _updateCheckCancellation.Token);
    }

    public UpdateCheckState UpdateState => _startupUpdates.State;

    public event Action<UpdateCheckState>? UpdateStateChanged;

    private void OnUpdateStateChanged(UpdateCheckState state) =>
        UpdateStateChanged?.Invoke(state);

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
