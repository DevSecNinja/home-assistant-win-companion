using Microsoft.Extensions.Logging;
using WindowsCompanion.Core.App;

namespace WindowsCompanion_App;

public sealed partial class AppController
{
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;

        var log = _loggerFactory.CreateLogger<AppController>();
        log.LogInformation("Stopping companion background services.");

        try
        {
            await _updateCheckCancellation.CancelAsync().ConfigureAwait(false);
            await _startupUpdates.CancelAsync().ConfigureAwait(false);
            await _updateInstaller.CancelAsync().ConfigureAwait(false);
            if (_updateCheckTask is not null)
                await _updateCheckTask.ConfigureAwait(false);

            _startupUpdates.StateChanged -= OnUpdateStateChanged;
            _updateInstaller.StateChanged -= OnUpdateInstallStateChanged;
            _network.NetworkChanged -= OnNetworkChanged;
            _network.Stop();
            Interlocked.Exchange(ref _networkSettle, null)?.Cancel();
            _lastNetwork = null;

            using (await _lifecycle.AcquireAsync(LifecycleIntent.Stop).ConfigureAwait(false))
            {
                await DisconnectCoreAsync().ConfigureAwait(false);
            }

            log.LogInformation("Companion background services stopped.");
        }
        finally
        {
            _updateCheckCancellation.Dispose();
            _lifecycle.Dispose();
            foreach (var dependency in _ownedDependencies.Reverse())
            {
                switch (dependency)
                {
                    case IAsyncDisposable asyncDisposable:
                        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                        break;
                    case IDisposable disposable:
                        disposable.Dispose();
                        break;
                }
            }
        }
    }
}
