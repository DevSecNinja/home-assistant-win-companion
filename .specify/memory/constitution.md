# Home Assistant Windows Companion Constitution

## Core Principles

### I. Native Windows Experience First

The application MUST feel like a first-class Windows app, not a wrapped web page.
Use the Windows App SDK (WinUI 3) with Fluent design, Mica/backdrop material,
light/dark theme that follows the system, and native affordances (system tray,
toast notifications). Any embedded web content (the Home Assistant frontend) is
hosted inside `WebView2`, but the shell, navigation, and OS integrations MUST be
native. Reject solutions that degrade the native experience purely for
cross-platform convenience.

### II. Security & Privacy of Credentials (NON-NEGOTIABLE)

Home Assistant access tokens and the derived `webhook_id`/`secret` are sensitive.
They MUST be stored using the Windows Credential Locker / DPAPI
(`PasswordVault` or `ProtectedData`), never in plaintext config, logs, or source
control. Network calls MUST validate TLS by default. No telemetry or user data
leaves the machine except calls to the user's own Home Assistant instance. Secrets
MUST NOT appear in exception messages or diagnostic output.

### III. Spec-Driven Development

Every feature starts as a specification under `specs/`, followed by a plan and a
task list, before implementation. Code MUST trace back to a requirement (FR-xxx)
or user story. Behavioral changes update the spec first. The Spec Kit workflow
(constitution -> specify -> plan -> tasks -> implement) is the source of truth
for scope.

### IV. Testable, Layered Architecture

Business logic (Home Assistant client, sensor collection, auth, storage) lives in
a platform-agnostic core library that is unit-testable without a UI. The WinUI app
is a thin presentation layer over that core. Integration points (HTTP, WebSocket,
OS sensors) sit behind interfaces so they can be faked in tests. New core contracts
require at least a unit test covering a happy path and a failure path.

### V. Resilience & Observability

The app runs long-lived in the background (system tray). It MUST tolerate network
loss, Home Assistant restarts, sleep/resume, and expired tokens by reconnecting
with backoff and surfacing clear, actionable status to the user. Structured
logging (no secrets) MUST be available to diagnose connection and sensor-sync
issues.

## Additional Constraints

- **Technology stack**: C# / .NET 9, Windows App SDK (WinUI 3), WebView2,
  `System.Text.Json`. Target Windows 10 build 19041+ and Windows 11.
- **Home Assistant integration**: Uses the documented native app integration
  (`/api/mobile_app/registrations`, webhook `register_sensor`/`update_sensor`) and
  the WebSocket API. No undocumented endpoints.
- **Dependencies**: Prefer the .NET BCL and first-party Microsoft packages. Add
  third-party packages only when they remove meaningful complexity.

## Development Workflow

- Specs, plans, and tasks are authored/updated before code for any behavioral change.
- The solution MUST build with `dotnet build` on a clean checkout.
- Core library unit tests MUST pass before a feature is considered done.
- Secrets and personal Home Assistant URLs MUST never be committed.

## Governance

This constitution supersedes ad-hoc practices for this repository. Amendments are
made by updating this file with a version bump and a short rationale. Complexity
that violates a principle MUST be justified in the plan's Complexity Tracking
section or rejected.

**Version**: 1.0.0 | **Ratified**: 2026-08-06 | **Last Amended**: 2026-08-06
