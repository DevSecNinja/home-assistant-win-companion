using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Updates;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace WindowsCompanion_App.Services;

/// <summary>
/// Shows native Windows toast notifications for Home Assistant notifications
/// using the Windows App SDK AppNotifications API (works unpackaged).
/// </summary>
public sealed class ToastNotifier : INotificationSink, IUpdateNotificationSink
{
    public void Show(NotificationMessage notification)
    {
        var builder = new AppNotificationBuilder()
            .AddText(string.IsNullOrWhiteSpace(notification.Title) ? "Home Assistant" : notification.Title);

        if (!string.IsNullOrWhiteSpace(notification.Message))
            builder.AddText(notification.Message);

        AppNotificationManager.Default.Show(builder.BuildNotification());
    }

    public void Show(AvailableUpdate update)
    {
        var releaseButton = new AppNotificationButton("View release");
        releaseButton.InvokeUri = update.ReleasePage;

        var notification = new AppNotificationBuilder()
            .AddText($"{Branding.ProductName} update available")
            .AddText(
                $"Installed: v{update.InstalledVersion}. Available: v{update.AvailableVersion}.")
            .AddButton(releaseButton)
            .BuildNotification();

        AppNotificationManager.Default.Show(notification);
    }
}
