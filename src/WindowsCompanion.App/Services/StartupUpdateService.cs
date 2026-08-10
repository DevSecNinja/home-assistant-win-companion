using Microsoft.Extensions.Logging;
using WindowsCompanion.Core.Updates;

namespace WindowsCompanion_App.Services;

/// <summary>Best-effort application boundary for the startup update notification.</summary>
internal sealed class StartupUpdateService
{
    private readonly InstalledBuildInfo _installed;
    private readonly StartupUpdateChecker _checker;
    private readonly ILogger<StartupUpdateService> _log;

    internal StartupUpdateService(
        InstalledBuildInfo installed,
        IReleaseSource releases,
        IUpdateNotificationSink notifications,
        ILogger<StartupUpdateService> log)
    {
        _installed = installed;
        _checker = new StartupUpdateChecker(releases, notifications);
        _log = log;
    }

    internal async Task CheckAsync(CancellationToken cancellationToken)
    {
        if (!_installed.IsOfficialRelease)
        {
            _log.LogDebug("Skipping the update check for a source or CI build.");
            return;
        }

        if (_installed.Version is null)
        {
            _log.LogWarning("Skipping the update check because the installed version is invalid.");
            return;
        }

        try
        {
            var notified = await _checker
                .CheckOnceAsync(_installed.Version, cancellationToken)
                .ConfigureAwait(false);
            if (!notified)
                _log.LogDebug("No newer stable release is available.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _log.LogDebug("The startup update check was cancelled during shutdown.");
        }
        catch (Exception ex)
        {
            // This is an explicitly best-effort startup boundary. A release service
            // failure is diagnostic-only and must never affect Home Assistant startup.
            _log.LogDebug(ex, "The startup update check failed.");
        }
    }
}
