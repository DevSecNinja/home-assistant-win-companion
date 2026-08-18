# Feature Specification: Notification State Sensor

**Status**: Shipped

Add a `user_notification_state` sensor that reports whether Windows considers the
PC busy, presenting, full-screen, or ready for notifications.

## Requirements

- Enabled by default as a diagnostic entity.
- Calls `SHQueryUserNotificationState` (shell32) to read the current state.
- Reports human-readable states: Not Present, Busy, Full Screen, Presentation,
  Accepts Notifications, Quiet Time, App.
- Exposes a `suppresses_notifications` boolean attribute for automations.
- Explicitly documents that Windows 11 Focus / Do Not Disturb is not included
  (no supported API for unpackaged apps) via an `includes_do_not_disturb: false`
  attribute.
- Polls every 10 seconds with push only on state change.
- State formatting and suppression logic live in Core (`NotificationStateFormatter`).

## Privacy

- No sensitive data. The sensor describes system presentation context only.
