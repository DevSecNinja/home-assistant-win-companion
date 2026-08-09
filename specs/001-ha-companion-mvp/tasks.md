---
description: "Task list for Home Assistant Windows Companion (MVP)"
---

# Tasks: Home Assistant Windows Companion (MVP)

> **Historical record:** This generated task list captures the original MVP plan.
> The feature shipped and subsequently evolved through features and fixes recorded
> in issues, commits, and `specs/002-sensor-catalog/`. The unchecked boxes are not
> outstanding work and are intentionally retained to avoid rewriting the original
> execution plan as if it had predicted the final implementation.

**Input**: Design documents from `/specs/001-ha-companion-mvp/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Unit tests for the core library are included (Principle IV).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 (connect + open HA), US2 (sensors), US3 (notifications)

## Path Conventions

Two-project solution: `src/WindowsCompanion.Core/`, `src/WindowsCompanion.App/`,
tests in `tests/WindowsCompanion.Core.Tests/`.

---

## Phase 1: Setup (Shared Infrastructure)

- [ ] T001 Create `WindowsCompanion.sln` and project skeletons: `src/WindowsCompanion.Core`
      (classlib, net9.0), `src/WindowsCompanion.App` (WinUI 3, net9.0-windows), and
      `tests/WindowsCompanion.Core.Tests` (xUnit). Add project references.
- [ ] T002 Add NuGet packages: App → `Microsoft.WindowsAppSDK`,
      `H.NotifyIcon.WinUI`; Core →
      `Microsoft.Extensions.Logging.Abstractions`; Tests → xUnit + runner.
- [ ] T003 [P] Add `.editorconfig` / nullable + implicit usings enabled in both
      projects; ensure `dotnet build` succeeds on empty skeleton.

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: Completes the core contracts and models all stories depend on.

- [ ] T004 [P] Create DTOs/models in `src/WindowsCompanion.Core/Models/`:
      `ServerConfig`, `DeviceRegistrationRequest`, `DeviceRegistrationResponse`,
      `Sensor`, `SystemStatus`, `NotificationMessage`, `ConnectionState`.
- [ ] T005 [P] Define abstractions in `src/WindowsCompanion.Core/Abstractions/`:
      `IHomeAssistantClient`, `ISecretStore`, `ISystemStatusProvider`, `IClock`.
- [ ] T006 [P] Implement secret redaction helpers in
      `src/WindowsCompanion.Core/Security/` and a `SystemDefaults`/settings loader
      (`%LOCALAPPDATA%\WindowsCompanion\settings.json`) with no-secret guarantees.
- [ ] T007 Implement `HomeAssistantClient` (REST + webhook) in
      `src/WindowsCompanion.Core/HomeAssistant/` using `HttpClient`: `ValidateAsync`
      (GET `/api/`), `RegisterDeviceAsync` (POST `/registrations`),
      `RegisterSensorAsync` / `UpdateSensorsAsync` (webhook).
- [ ] T008 Implement `ConnectionManager` skeleton in `src/WindowsCompanion.Core/App/`
      holding `ConnectionState` with reconnect/backoff scaffolding + logging.

**Checkpoint**: Core compiles; models + client + interfaces ready.

---

## Phase 3: User Story 1 - Connect & open Home Assistant (P1) 🎯 MVP

### Tests for US1

- [ ] T009 [P] [US1] Unit tests for `HomeAssistantClient.ValidateAsync` and request
      construction (base URL handling, bearer header) using a fake HTTP handler in
      `tests/WindowsCompanion.Core.Tests/HomeAssistantClientTests.cs`.
- [ ] T010 [P] [US1] Unit tests for settings load/save round-trip and that secrets
      are never serialized to settings.json.

### Implementation for US1

- [ ] T011 [US1] Implement `WindowsSecretStore` (PasswordVault) in
      `src/WindowsCompanion.App/Services/`.
- [ ] T012 [US1] Implement the OAuth loopback login: `LoopbackOAuthListener`
      (fixed-port `TcpListener` capturing the `code`) and `OAuthLoginService`
      (build authorize URL, open browser, exchange code) in
      `src/WindowsCompanion.App/Services/`.
- [ ] T013 [US1] Implement `MainWindow` with a Connect view (URL entry + "Sign in")
      and a lean Status view (connection state, battery, "Open Home Assistant" that
      launches BaseUrl in the default browser, "Disconnect"). No embedded web view.
- [ ] T014 [US1] Wire app bootstrap via `AppController`: on launch, if a saved config
      + refresh token exist, resume the session and show the Status view; else show
      the Connect view.
- [ ] T015 [US1] Persist `ServerConfig` (non-secret) and the OAuth refresh token
      (secret store) on successful sign-in; auto-resume on relaunch.

**Checkpoint**: US1 fully functional — sign in once, session resumes on relaunch and
"Open Home Assistant" launches the browser.

---

## Phase 4: User Story 2 - Report PC status sensors (P2)

### Tests for US2

- [ ] T016 [P] [US2] Unit tests for `BatterySensorProvider` mapping
      `SystemStatus` → `battery_level`/`battery_state` sensor payloads (charging,
      discharging, no-battery) in `tests/.../BatterySensorProviderTests.cs`.
- [ ] T017 [P] [US2] Unit tests for `SensorSyncService` register-once-then-update
      logic and re-register-on-unknown behavior (fake `IHomeAssistantClient`).

### Implementation for US2

- [ ] T018 [US2] Implement `WindowsSystemStatusProvider` in
      `src/WindowsCompanion.App/Services/` via P/Invoke `GetSystemPowerStatus`.
- [ ] T019 [P] [US2] Implement `BatterySensorProvider` in
      `src/WindowsCompanion.Core/Sensors/` producing the two sensors from `SystemStatus`.
- [ ] T020 [US2] Implement `SensorSyncService` in `src/WindowsCompanion.Core/Sensors/`:
      ensure device registered, register sensors once, then `update` on a timer.
- [ ] T021 [US2] On successful connect, call `RegisterDeviceAsync` if not yet
      registered; start `SensorSyncService`; refresh on power-mode change.

**Checkpoint**: PC + battery sensors appear in HA and update.

---

## Phase 5: User Story 3 - HA notifications as toasts (P3)

### Tests for US3

- [ ] T022 [P] [US3] Unit tests for `HaWebSocketClient` message framing (auth
      handshake, subscribe, event parsing → `NotificationMessage`) using a fake
      duplex stream in `tests/.../HaWebSocketClientTests.cs`.

### Implementation for US3

- [ ] T023 [US3] Implement `HaWebSocketClient` in `src/WindowsCompanion.Core/HomeAssistant/`:
      connect, auth, open `mobile_app/push_notification_channel`, confirm each
      delivery via `mobile_app/push_notification_confirm`, emit
      `NotificationMessage` events; ping/pong liveness. Declare
      `app_data.push_websocket_channel` at registration so the PC is a notify target.
- [ ] T024 [US3] Integrate WS lifecycle into `ConnectionManager` with reconnect +
      re-open of the push channel and `AuthError` handling.
- [ ] T025 [US3] Implement `ToastNotifier` in `src/WindowsCompanion.App/Services/` using
      Windows App SDK `AppNotification`; show title/message; activation restores window.
- [ ] T026 [US3] Bridge WS `NotificationMessage` events → `ToastNotifier` in the app.

**Checkpoint**: HA notifications appear as Windows toasts; survive reconnects.

---

## Phase 6: Polish & Cross-Cutting

- [ ] T027 [P] Implement `TrayIconService` (`H.NotifyIcon.WinUI`): show/hide window,
      connection-status tooltip, Disconnect, Exit; minimize-to-tray behavior.
- [ ] T028 Implement Disconnect/sign-out: clear secrets + settings, return to ConnectView.
- [ ] T029 [P] Add exponential-backoff + jitter reconnection and power/session-resume
      hooks in `ConnectionManager`; verify AuthError stops the retry loop.
- [ ] T030 [P] Redacting logging setup; verify no secrets in logs (unit check).
- [ ] T031 Run `quickstart.md` validation end-to-end; update README.

---

## Dependencies & Execution Order

- **Setup (T001–T003)** → **Foundational (T004–T008)** → user stories.
- **US1 (T009–T015)** is the MVP and can ship alone.
- **US2 (T016–T021)** and **US3 (T022–T026)** depend only on Foundational + US1's
  connection wiring; can be built in parallel after US1.
- **Polish (T027–T031)** last.

### Parallel opportunities

- T004, T005, T006 in parallel (different files).
- All `[P]` test tasks in parallel.
- After US1, US2 and US3 implementation can proceed in parallel.

## Implementation Strategy

1. Setup + Foundational.
2. US1 → validate (MVP: sign in + open HA in browser + resume on relaunch).
3. US2 → validate sensors in HA.
4. US3 → validate toasts.
5. Polish (tray, sign-out, resilience, logging, README).
