<!--
Sync Impact Report
- Version change: 1.0.0 -> 2.0.0
- Modified principles:
  - I. Native Windows Experience First: removed the obsolete WebView2 requirement
    and made opening Home Assistant in the default browser the governing design.
  - III. Spec-Driven Development -> Evidence-Driven Development: replaced the
    mandatory artifact sequence with proportional, continuously corrected design
    records based on implementation and protocol evidence.
- Added sections: none.
- Removed sections: none.
- Follow-up TODOs: none.
-->

# Home Assistant Windows Companion Constitution

## Core Principles

### I. Native Windows Experience First

The application MUST feel like a first-class Windows app, not a wrapped web page.
Use the Windows App SDK (WinUI 3) with Fluent design, Mica/backdrop material,
light/dark theme that follows the system, and native affordances (system tray,
toast notifications). The companion MUST remain a focused native utility rather
than embedding the Home Assistant frontend; actions that open Home Assistant MUST
use the user's default browser. Reject solutions that degrade the native
experience purely for cross-platform convenience.

### II. Security & Privacy of Credentials (NON-NEGOTIABLE)

Home Assistant access tokens and the derived `webhook_id`/`secret` are sensitive.
They MUST be stored using the Windows Credential Locker / DPAPI
(`PasswordVault` or `ProtectedData`), never in plaintext config, logs, or source
control. Network calls MUST validate TLS by default. No telemetry or user data
leaves the machine except calls to the user's own Home Assistant instance. Secrets
MUST NOT appear in exception messages or diagnostic output.

### III. Evidence-Driven Development

User-visible features and consequential protocol decisions MUST be recorded under
`specs/` so their intent and evidence survive implementation. Planning artifacts
MUST be proportional to the change: a full specification, plan, and task list are
required for large or ambiguous features, while focused fixes MAY use an issue and
targeted documentation. Discoveries made against real Windows or Home Assistant
behavior MUST be written back promptly to the relevant specification, contract, or
research record. Shipped behavior and verified upstream behavior take precedence
over an earlier plan; stale design documents MUST be corrected rather than treated
as authority.

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

- **Technology stack**: C# / .NET 9, Windows App SDK (WinUI 3),
  `System.Text.Json`. Target Windows 10 build 19041+ and Windows 11.
- **Home Assistant integration**: Uses the documented native app integration
  (`/api/mobile_app/registrations`, webhook `register_sensor`/`update_sensor_states`) and
  the WebSocket API. No undocumented endpoints.
- **Dependencies**: Prefer the .NET BCL and first-party Microsoft packages. Add
  third-party packages only when they remove meaningful complexity.

## Development Workflow

- Large or ambiguous features have a specification, plan, and task list before
  implementation. Focused changes MUST still have written scope in an issue or
  specification.
- Implementation discoveries MUST be reflected in the relevant design records
  before the change is considered complete.
- The solution MUST build with `dotnet build` on a clean checkout.
- Core library unit tests MUST pass before a feature is considered done.
- Secrets and personal Home Assistant URLs MUST never be committed.

## Governance

This constitution supersedes ad-hoc practices for this repository. Amendments MUST
update this file with a semantic version bump, amendment date, and Sync Impact
Report. Compliance MUST be reviewed when planning substantial features and when
cross-checking shipped behavior against design records. Complexity that violates a
principle MUST be justified in the feature plan or issue and explicitly approved,
or rejected.

**Version**: 2.0.0 | **Ratified**: 2026-08-06 | **Last Amended**: 2026-08-07
