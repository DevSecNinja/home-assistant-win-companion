# Implementation Plan: Home Assistant Windows Companion (MVP)

**Branch**: `001-ha-companion-mvp` | **Date**: 2026-08-06 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-ha-companion-mvp/spec.md`

## Summary

Build a native Windows desktop companion for Home Assistant using WinUI 3 (Windows
App SDK). A platform-agnostic core library (`HaCompanion.Core`) implements the Home
Assistant client (REST registration + webhook sensor updates + WebSocket events),
secure credential storage abstraction, and a battery/status sensor provider. The
WinUI app (`HaCompanion.App`) is a lean, tray-resident companion: it does **not**
embed the Home Assistant frontend, but offers an "Open Home Assistant" button that
launches the instance in the user's default browser. It adds a system tray icon,
shows native toast notifications, and wires OS power events. Authentication uses
**OAuth2 (IndieAuth) with a loopback redirect** — the user signs in through their
browser and no tokens are pasted; the PC registers as a `mobile_app` device and
reports battery-level and battery-state sensors.

## Technical Context

**Language/Version**: C# 13 / .NET 9

**Primary Dependencies**: Windows App SDK (WinUI 3) `Microsoft.WindowsAppSDK`,
`System.Text.Json`, `Microsoft.Windows.SDK.BuildTools`,
`System.Net.WebSockets.Client` (BCL), `System.Net.Sockets.TcpListener` (BCL, for the
OAuth loopback listener). Tray icon via `H.NotifyIcon.WinUI`. Toasts
via Windows App SDK `AppNotification` API. Tests via `xUnit`.

**Storage**: Windows Credential Locker (`Windows.Security.Credentials.PasswordVault`)
for the OAuth refresh token, webhook id, and cloudhook URL; a small JSON app-settings
file (`%LOCALAPPDATA%\HaCompanion\settings.json`) for non-secret config (base URL,
device id, sensor preferences, and registered-sensor metadata).

**Testing**: xUnit unit tests for the core library (client request building, sensor
provider, settings/registration models) with faked HTTP/OS interfaces.

**Target Platform**: Windows 10 build 19041+ and Windows 11 (x64 and ARM64; the
source run script currently builds x64).

**Project Type**: Desktop application (native tray-resident companion; no embedded
web view) with a reusable core library.

**Performance Goals**: Companion window visible within ~2s of a warm launch; sensor
update CPU cost negligible (<1% average); toast latency <10s while connected.

**Constraints**: No secrets in logs or on-disk plaintext; TLS validated for
non-local hosts; must run minimized in the tray and survive sleep/resume and
network loss via reconnect-with-backoff.

**Scale/Scope**: Single Home Assistant server; a handful of sensors; one user per
machine profile.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Native Windows Experience First**: PASS — WinUI 3 shell, Fluent theme,
  system tray, native toasts; Home Assistant itself opens in the default browser
  (no embedded web content to theme or secure).
- **II. Security & Privacy (NON-NEGOTIABLE)**: PASS — tokens/secrets in Credential
  Locker; no secrets in logs; TLS validated for non-local hosts. Storage sits
  behind `ISecretStore` so it is testable and swappable.
- **III. Spec-Driven Development**: PASS — spec + plan + tasks precede code.
- **IV. Testable, Layered Architecture**: PASS — `HaCompanion.Core` has no UI
  dependency; HTTP/WebSocket/OS behind interfaces (`IHomeAssistantClient`,
  `ISecretStore`, `ISystemStatusProvider`, `IClock`).
- **V. Resilience & Observability**: PASS — reconnect-with-backoff, connection
  state surfaced, structured logging via `Microsoft.Extensions.Logging` with a
  secret-redacting policy.

No violations — Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/001-ha-companion-mvp/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (HA API contracts used)
│   ├── registration.md
│   ├── sensors.md
│   └── websocket.md
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
HaCompanion.sln

src/
├── HaCompanion.Core/            # Platform-agnostic library (unit-testable)
│   ├── Models/                  # Registration, Sensor, ServerConfig, Notification DTOs
│   ├── Abstractions/            # IHomeAssistantClient, ISecretStore, ISystemStatusProvider, IClock
│   ├── HomeAssistant/           # HomeAssistantClient (REST + webhook), HaWebSocketClient
│   ├── Sensors/                 # BatterySensorProvider, SensorSyncService
│   ├── Security/                # Secret redaction helpers
│   └── App/                     # ConnectionManager (orchestration + reconnect/backoff)
│
└── HaCompanion.App/             # WinUI 3 (Windows App SDK) desktop app
    ├── App.xaml(.cs)            # App bootstrap, tray lifecycle, toast registration
    ├── AppController.cs         # Coordinator: OAuth session, registration, connection
    ├── MainWindow.xaml(.cs)     # Connect view + lean Status view; tray icon
    ├── Services/                # WindowsSecretStore, WindowsSystemStatusProvider,
    │                            # ToastNotifier, OAuthLoginService, LoopbackOAuthListener,
    │                            # DeviceInfo, AppConstants
    └── Assets/

tests/
└── HaCompanion.Core.Tests/      # xUnit tests for core library
```

**Structure Decision**: Two-project solution. `HaCompanion.Core` holds all logic
behind interfaces so it is unit-testable with no Windows UI dependency (satisfies
Principle IV). `HaCompanion.App` is the thin WinUI 3 presentation + OS-integration
layer and provides the concrete Windows implementations of the core abstractions.

## Architecture & Key Decisions

1. **Auth (MVP)**: OAuth2 (IndieAuth) with a loopback redirect. The app opens
   `/auth/authorize` in the default browser with `client_id == redirect_uri ==
   http://localhost:<fixed-port>/`; a local `TcpListener` captures the returned
   `code`, which is exchanged at `/auth/token` for an access + refresh token. The
   refresh token is stored in the Credential Locker; access tokens are refreshed on
   demand. A **fixed** loopback port is required because HA validates that the
   refresh grant's `client_id` matches the authorization `client_id`.
2. **Device registration**: On first successful connect, POST
   `/api/mobile_app/registrations` with `app_id=io.homeassistant.windows`, a stable
   `device_id` (GUID persisted), OS/model metadata, `supports_encryption=false`.
   Persist `webhook_id` and `cloudhook_url` in the Credential Locker; persist the
   non-secret `remote_ui_url` in settings.
3. **Sensor reporting**: Use webhook `POST /api/webhook/<webhook_id>` with
   `register_sensor` then periodic `update_sensor_states`. Sensors: `battery_level`
   (device_class battery, unit %) and `battery_state` (charging/discharging/…).
   Update on a timer and on OS power-status change.
4. **Notifications**: Maintain a WebSocket connection (`/api/websocket`), authenticate
   with the access token, and open a `mobile_app/push_notification_channel` (local
   push) to render Windows toasts, confirming each delivery. Registration declares
   `app_data.push_websocket_channel` so the PC becomes a notify target. This
   replaces mobile push, which Windows lacks an equivalent for.
5. **Open Home Assistant**: Instead of embedding a web view, the companion provides
   an "Open Home Assistant" action (window button + tray menu) that launches the
   configured base URL in the user's default browser via `Process.Start` with
   `UseShellExecute`.
6. **Resilience**: `ConnectionManager` owns state (Disconnected, Connecting,
   Connected, Reconnecting, AuthError) and reconnects the WebSocket and sensor sync
   with exponential backoff; listens to power/session resume.
7. **Security**: `WindowsSecretStore` wraps `PasswordVault` (stores the OAuth refresh
   token). Logging uses a redaction layer; DTOs never `ToString()` secrets.

## Complexity Tracking

No constitution violations; section intentionally empty.
