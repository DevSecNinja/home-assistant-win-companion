using WindowsCompanion.Core.Models;

namespace WindowsCompanion.Core.Abstractions;

public interface IWinGetUpdateProvider
{
    Task<WinGetCapabilityResult> ProbeCapabilityAsync(
        CancellationToken cancellationToken = default);

    Task<WinGetUpdateResult> CheckForUpdatesAsync(
        CancellationToken cancellationToken = default);
}
