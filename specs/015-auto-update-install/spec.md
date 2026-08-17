# Feature Specification: Auto-Update Download and Install

**Status**: Shipped

**Input**: Issue #161, "feat(updating): implement auto updating".

`specs/011-startup-update-checks/spec.md` covers *checking* for a newer release.
This spec covers what happens once a newer release is known: downloading the
matching setup package, verifying it, and installing it with an explicit,
user-triggered action. The companion is never fully unattended — it always
requires the user to click **Install now** (or the equivalent tray/Settings
action) before anything is installed or the app restarts.

## Update mode preference

Update behavior is configurable in Settings, under a new **Update mode**
control with three options, persisted per account in
`%LOCALAPPDATA%\WindowsCompanion\settings.json` (`Updates.Mode`, non-secret):

- **Auto-install updates** (default) — the existing startup/periodic check
  behavior from spec 011 runs as before. When a newer release is found, the
  matching setup package is downloaded and verified in the background with no
  user action required. Once verified, the user is offered an explicit
  **Install now** action (tray, top banner, and Settings) that silently runs
  the installer and restarts the app.
- **Notify only** — checks still run, and the badge/toast/banner still appear,
  but nothing is downloaded automatically. The available-update surface offers
  **View release**, which opens the release page in the browser, exactly as
  described in spec 011.
- **Don't check for updates** — no requests are made to GitHub at all. No
  check is scheduled at startup and the user-initiated **Check for
  updates…** action is unavailable.

Selecting a mode takes effect immediately: switching away from **Auto-install
updates** does not cancel an in-flight download, but it does prevent new
automatic downloads from starting; switching to **Don't check for updates**
stops future checks (an update already found is still shown until the app
restarts).

Existing installs without a persisted preference default to **Auto-install
updates**, matching the intent of issue #161 that this be the default,
low-friction path for most users.

## Release asset discovery

- Each GitHub release publishes, per supported architecture (`x64`,
  `arm64`), a setup ZIP (`WindowsCompanion-<version>-win-<arch>-setup.zip`),
  a `.sha256` checksum sidecar, an SPDX SBOM, and a GitHub build-provenance
  attestation whose subject is the setup ZIP.
- The running process architecture (`RuntimeInformation.ProcessArchitecture`)
  selects the matching asset; `Arm64` maps to the `arm64` asset, everything
  else (including `X64` and any other reported architecture) maps to `x64`.
- If no matching asset is published for a release (e.g. a future architecture
  not yet supported by client selection), the update falls back to
  notify-only behavior for that release: the user can still open the release
  page manually, but no automatic download is offered.

## Download

- The setup ZIP is streamed to
  `%LOCALAPPDATA%\WindowsCompanion\Updates\<version>\` with reported progress.
- Downloads are single-flight and cancellation-safe: a superseding update
  check, a mode change away from auto-install, or app shutdown cancels any
  in-progress download and cleans up partial files.

## Verification (fail-closed)

Both checks must pass before an update is offered for install; either
failing marks the update `Failed` with a user-visible reason and leaves the
existing "open the release page" action as the manual fallback. Nothing is
ever installed unverified.

1. **Checksum** — the `.sha256` sidecar is downloaded and parsed
   (`<hash>  <filename>`), and compared against a streaming SHA256 of the
   downloaded ZIP.
2. **Build provenance attestation** — the GitHub attestations API
   (`GET /repos/DevSecNinja/home-assistant-win-companion/attestations/sha256:<digest>`)
   is queried for the exact file digest, and the returned Sigstore bundle is
   verified, including that the signing certificate's OIDC issuer,
   repository, and workflow claims match this repository's release workflow.

## Install and relaunch

- **Install now** (available once verification succeeds) extracts the
  verified ZIP, confirms `setup.exe` and its parts are present, and hands off
  to a detached PowerShell helper script rather than running the installer
  from inside the process it is about to close.
- The helper script waits for the current process to exit, runs
  `setup.exe /VERYSILENT /SP- /NORESTART`, records the outcome (success or
  failure, version, exit code — never a Home Assistant URL, credential, or
  sensor data) to
  `%LOCALAPPDATA%\WindowsCompanion\Updates\last-install.json`, and relaunches
  `WindowsCompanion.exe` from its known per-user install directory on
  success.
- The app then runs its existing graceful shutdown path; it never vetoes or
  skips normal teardown to force an install.
- On the next startup, `last-install.json` is read once and deleted, showing
  a one-time success banner ("Updated to version X") or a failure banner
  (with the existing "open the release page" fallback) if the silent install
  did not complete.

## UI surfaces

- The top banner, tray menu, and Settings "Install update" control share one
  presentation model (`UpdateStatusPresentation`) that reflects both the
  release-check state (spec 011) and the install state
  (`Downloading NN%` / `Verifying…` / `Ready to install` / `Installing…` /
  post-install success or failure).
- Before an update is ready to install, these controls only show progress —
  no destructive action is ever one click away.
- Once ready, the banner exposes a dedicated **Install now** button and the
  Settings control becomes an enabled **Install now** button; both call the
  same install action.
- If the active mode is not **Auto-install updates**, or verification failed,
  the Settings control falls back to **View release**, which opens the
  browser exactly as in notify-only mode.

## Non-goals

- No changes to `.github/workflows/release.yml` — this feature only consumes
  the checksum sidecars and attestations the workflow already publishes.
- No fully unattended/silent update path with no user action; issue #161's
  "background download, explicit install" design was chosen deliberately over
  a single on-demand button or a fully silent update.
