# Quickstart: Mocked Home Assistant End-to-End and UI Tests

## Prerequisites

- Windows 10/11 development machine
- .NET 10 SDK and the repository's matching Windows SDK
- Windows App Runtime 2.3
- For UI tests: an unlocked interactive desktop

No Home Assistant installation, account, token, or personal URL is required.

## Validation policy

All further runtime test execution is CI-only by user request. Do not run the
repository test commands locally as part of this feature's remaining validation.
Use hosted and trusted interactive workflow results and retained artifacts.

## Headless end-to-end validation

Hosted Windows CI:

- starts a scenario-scoped Kestrel server on an OS-assigned loopback port;
- uses only synthetic OAuth, registration, webhook, and WebSocket values;
- runs the headless E2E project and compiles the UI automation project; and
- builds and publishes Release application artifacts for x64 and ARM64.

Prior runtime validation passed 24 journey and foundation tests. No authoritative
duration was retained, so timing and repeatability goals remain CI-owned.

## Native UI validation

The rendered UI workflow targets a self-hosted runner with the labels `windows`,
`x64`, and `interactive`. Configure the runner under a dedicated, auto-logged-in
test account and start the Actions runner from that user's desktop session rather
than installing it as a Windows service. Keep the session unlocked, install the
matching Windows App Runtime, and prevent unrelated workloads from using the
desktop while tests run.

The fixture launches the Debug test composition with an isolated profile and fake
server. Prior interactive validation passed seven rendered UI scenarios and the
native-toast capability scenario. The tray scenario is capability-gated and skips
with the missing capability when UIA cannot expose a usable tray icon; it is never
converted into a pass or driven with screen coordinates.

This repository is public. Never allow unreviewed fork pull-request code to run on
the persistent self-hosted runner. The checked-in UI workflow runs only for trusted
pushes to `main` or explicit manual dispatch. Use repository/environment approval
rules if additional triggers are added later.

## Failure evidence

Failed end-to-end and UI scenarios write sanitized evidence under the test
results directory:

- TRX test result
- fake-server interaction log
- isolated application log
- UI screenshot and accessibility-tree summary for UI failures

Evidence must contain only synthetic endpoints and credentials. If a retained
artifact includes a personal URL, credential, webhook identifier, Wi-Fi
identifier, or sensitive sensor value, treat that as a test failure.

## Continuous integration

- Hosted Windows CI builds x64/ARM64, runs Core and headless end-to-end tests.
- The UI project is compiled on hosted CI.
- Rendered UI smoke tests run sequentially on an interactive self-hosted Windows
  runner for trusted pushes to `main` or manual dispatch.
