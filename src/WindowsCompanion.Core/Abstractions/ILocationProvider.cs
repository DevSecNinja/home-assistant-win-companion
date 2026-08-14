using WindowsCompanion.Core.Models;

namespace WindowsCompanion.Core.Abstractions;

public interface ILocationProvider
{
    Task<LocationResult> GetLocationAsync(CancellationToken cancellationToken = default);
}
