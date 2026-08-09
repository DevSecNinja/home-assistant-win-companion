namespace WindowsCompanion.Core.Sensors;

/// <summary>An expensive source that can be refreshed explicitly before a manual push.</summary>
public interface IRefreshableSensorSource
{
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
