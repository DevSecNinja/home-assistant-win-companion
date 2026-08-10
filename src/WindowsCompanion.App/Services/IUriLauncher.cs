namespace WindowsCompanion_App.Services;

/// <summary>Launches a URI through the platform shell.</summary>
public interface IUriLauncher
{
    Task LaunchAsync(Uri uri, CancellationToken cancellationToken = default);
}
