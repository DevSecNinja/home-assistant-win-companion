# Home Assistant Windows Companion

A lean, native Windows companion for [Home Assistant](https://www.home-assistant.io/) —
the Windows counterpart to the official [iOS/macOS app](https://github.com/home-assistant/iOS).

It is **not** a dashboard. It stays out of your way in the system tray, reports your
PC's status to Home Assistant, turns Home Assistant notifications into native Windows
toasts, and opens Home Assistant in your normal browser when you want the UI.

## Features (MVP)

- **Browser-based sign-in** — OAuth2 (IndieAuth) with a loopback redirect. No
  long-lived tokens to create or paste. The refresh token is stored in the Windows
  Credential Locker.
- **Tray-resident** — closing the window hides it to the notification area.
- **Windows toasts** — notifications sent to this PC from Home Assistant appear as
  native toasts, delivered over the `mobile_app` local push channel (Windows has no
  APNS/FCM equivalent).
- **Status sensors** — reports battery level and battery state back to Home
  Assistant as a registered `mobile_app` device.
- **Open Home Assistant** — one click (window or tray menu) to open your instance in
  the default browser.

## Requirements

- Windows 10 (build 19041+) or Windows 11
- [.NET 9 SDK](https://dotnet.microsoft.com/download) to build
- **Windows App Runtime 2.3** — the app ships unpackaged and uses the Windows App SDK
  bootstrapper. Without it the app exits at startup with `REGDB_E_CLASSNOTREG`. The
  MSIX packages ship inside the `Microsoft.WindowsAppSDK.Runtime` NuGet package under
  `tools/MSIX/win10-x64/` and can be installed with `Add-AppxPackage`.
- A Home Assistant instance with the `mobile_app` integration (part of
  `default_config`)

## Build and run

```powershell
dotnet build HaCompanion.sln -c Debug
dotnet test tests/HaCompanion.Core.Tests/HaCompanion.Core.Tests.csproj
.\src\HaCompanion.App\bin\Debug\net9.0-windows10.0.26100.0\win-x64\HaCompanion.App.exe
```

On first launch, enter your Home Assistant URL and click **Sign in** — your browser
opens for login, then the app registers this PC and connects.

## Architecture

| Project | Purpose |
| --- | --- |
| `src/HaCompanion.Core` | Platform-agnostic logic: HA REST/webhook client, OAuth, WebSocket protocol, sensors, reconnect. No UI dependency, fully unit-tested. |
| `src/HaCompanion.App` | WinUI 3 (Windows App SDK) shell: OAuth loopback listener, tray icon, toasts, Credential Locker, battery via `GetSystemPowerStatus`. |
| `tests/HaCompanion.Core.Tests` | xUnit tests for the core library. |

Secrets live only in the Windows Credential Locker. Non-secret config
(base URL, device id, webhook id) goes to
`%LOCALAPPDATA%\HaCompanion\settings.json`.

## Notes on the Home Assistant APIs used

A few behaviours are easy to get wrong and are worth calling out:

- **Loopback OAuth needs a fixed port.** Home Assistant validates that the refresh
  grant's `client_id` matches the one used at authorization. Since `client_id` *is*
  the loopback redirect URL, an ephemeral port would silently break token refresh.
- **Use HTTPS.** HTTP redirects rewrite `POST` as `GET`, so a `POST /auth/token` sent
  to an `http://` URL that redirects arrives as a `GET` and fails with
  `405 Method Not Allowed`.
- **Notifications use the local push channel.** Home Assistant does *not* fire a
  `persistent_notification` event on the event bus (it uses an internal dispatcher
  signal), so `subscribe_events` for it never fires. Registering with
  `app_data.push_websocket_channel = true` is what makes the PC a notify target;
  notifications then arrive over `mobile_app/push_notification_channel` and must be
  acknowledged within 10s.

See [`specs/001-ha-companion-mvp/contracts/`](specs/001-ha-companion-mvp/contracts/)
for the full API contracts.

## Spec-driven development

This project is built with [GitHub Spec Kit](https://github.com/github/spec-kit).
The specification, plan, research, data model, API contracts and task breakdown live
in [`specs/001-ha-companion-mvp/`](specs/001-ha-companion-mvp/), and the project
principles in [`.specify/memory/constitution.md`](.specify/memory/constitution.md).

## Status

MVP, verified against a live Home Assistant instance: browser sign-in, device
registration, session resume, battery sensors updating, and notifications arriving
as Windows toasts.

## License

Not yet licensed.
