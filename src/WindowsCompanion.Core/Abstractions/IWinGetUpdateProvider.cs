using WindowsCompanion.Core.Models;

namespace WindowsCompanion.Core.Abstractions;

public interface IWinGetUpdateProvider
{
    Task<bool> IsModuleInstalledAsync(CancellationToken cancellationToken = default);

    Task<WinGetUpdateResult> CheckForUpdatesAsync(
        CancellationToken cancellationToken = default);
}
