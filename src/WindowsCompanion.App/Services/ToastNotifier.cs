using WindowsCompanion.Core.Models;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace WindowsCompanion_App.Services;

/// <summary>
/// Shows native Windows toast notifications for Home Assistant notifications
/// using the Windows App SDK AppNotifications API (works unpackaged).
/// </summary>
public sealed class ToastNotifier
{
    public void Show(NotificationMessage notification)
    {
        var builder = new AppNotificationBuilder()
            .AddText(string.IsNullOrWhiteSpace(notification.Title) ? "Home Assistant" : notification.Title);

        if (!string.IsNullOrWhiteSpace(notification.Message))
            builder.AddText(notification.Message);

        AppNotificationManager.Default.Show(builder.BuildNotification());
    }
}
