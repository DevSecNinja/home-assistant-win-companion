namespace HaCompanion.Core.Models;

/// <summary>An inbound notification from Home Assistant to render as a toast.</summary>
public sealed record NotificationMessage(string Title, string Message);

/// <summary>High-level connection lifecycle state surfaced to the UI and tray.</summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    AuthError
}
