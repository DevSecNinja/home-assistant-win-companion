using System.Diagnostics;

namespace WindowsCompanion_App.Services;

/// <summary>Opens URIs with the user's registered Windows application.</summary>
public sealed class ShellUriLauncher : IUriLauncher
{
    public Task LaunchAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        cancellationToken.ThrowIfCancellationRequested();

        Process.Start(new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true
        });

        return Task.CompletedTask;
    }
}
