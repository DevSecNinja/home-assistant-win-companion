using Microsoft.Extensions.Logging;
using WindowsCompanion.Core.Updates;

namespace WindowsCompanion_App.Services;

/// <summary>Best-effort application boundary for the startup update notification.</summary>
internal sealed class StartupUpdateService
{
    private readonly InstalledBuildInfo _installed;
    private readonly StartupUpdateChecker? _checker;
    private readonly ILogger<StartupUpdateService> _log;
    private readonly object _stateGate = new();
    private UpdateCheckState _state;
    private long _fallbackRevision;

    internal StartupUpdateService(
        InstalledBuildInfo installed,
        IReleaseSource releases,
        IUpdateNotificationSink notifications,
        ILogger<StartupUpdateService> log)
    {
        _installed = installed;
        _log = log;
        var fallbackVersion = ParseFallbackVersion();
        _state = new(
            UpdateCheckStatus.Idle,
            UpdateCheckTrigger.Automatic,
            installed.Version ?? fallbackVersion);

        if (installed.IsOfficialRelease && installed.Version is not null)
        {
            _checker = new StartupUpdateChecker(installed.Version, releases, notifications);
            _checker.StateChanged += OnStateChanged;
            _state = _checker.State;
        }
    }

    internal UpdateCheckState State => Volatile.Read(ref _state);

    internal event Action<UpdateCheckState>? StateChanged;

    internal async Task CheckAsync(
        UpdateCheckTrigger trigger,
        CancellationToken cancellationToken)
    {
        if (_checker is null)
        {
            if (trigger == UpdateCheckTrigger.Automatic)
            {
                _log.LogDebug("Skipping the update check for a source or CI build.");
                return;
            }

            var message = _installed.IsOfficialRelease
                ? "This build does not have a valid release version."
                : "Update checks are available in official release builds.";
            var revision = Interlocked.Increment(ref _fallbackRevision);
            OnStateChanged(new(
                UpdateCheckStatus.Checking,
                trigger,
                State.InstalledVersion,
                Revision: revision));
            OnStateChanged(new(
                UpdateCheckStatus.Error,
                trigger,
                State.InstalledVersion,
                ErrorMessage: message,
                Revision: revision));
            return;
        }

        try
        {
            var state = await _checker
                .CheckAsync(trigger, cancellationToken)
                .ConfigureAwait(false);
            if (state.Status == UpdateCheckStatus.Current)
                _log.LogDebug("No newer stable release is available.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _log.LogDebug("The startup update check was cancelled during shutdown.");
        }
        catch (OperationCanceledException)
        {
            _log.LogDebug("The update check was superseded by a newer request.");
        }
        catch (Exception ex)
        {
            // This is an explicitly best-effort startup boundary. A release service
            // failure is diagnostic-only and must never affect Home Assistant startup.
            _log.LogDebug(ex, "The startup update check failed.");
        }
    }

    internal Task CancelAsync() => _checker?.CancelAsync() ?? Task.CompletedTask;

    private void OnStateChanged(UpdateCheckState state)
    {
        lock (_stateGate)
        {
            if (state.Revision < _state.Revision) return;
            Volatile.Write(ref _state, state);
            StateChanged?.Invoke(state);
        }
    }

    private static SemanticVersion ParseFallbackVersion()
    {
        if (!SemanticVersion.TryParse("0.0.0", out var version))
            throw new InvalidOperationException("The fallback semantic version is invalid.");
        return version!;
    }
}
