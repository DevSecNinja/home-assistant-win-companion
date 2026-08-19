# Feature Specification: Startup Update Checks

**Status**: Shipped

**Input**: Issues #116, "Add startup update checks", and #119,
"Improve tray update actions and app activation".

Released builds make one non-blocking request at process startup and also support
fresh user-initiated checks. This spec covers the check-only behavior; downloading,
verifying, and silently installing an update is a user-configurable, opt-in
extension of this flow and is covered by
`specs/015-auto-update-install/spec.md`.

## Requirements

- The check starts only after Windows app notifications are registered and is not
  awaited by window creation, saved-session resume, or Home Assistant connection.
- The process exposes reusable `idle`, `checking`, `current`, `available`, and
  `error` state independently of the tray and banner so another app surface can
  present the same result without making a second request.
- A new user check cancels and supersedes an older check. Release requests remain
  single-flight, stale completions cannot replace newer state, and the same
  available version produces at most one Windows toast per process.
- The source is GitHub's public Releases REST endpoint for
  `DevSecNinja/home-assistant-win-companion`. The request uses HTTPS, identifies the
  product, asks for GitHub JSON, pins an API version, rejects redirects, and has a
  five-second timeout.
- SemVer parsing, precedence, draft/prerelease filtering, trusted release-page
  validation, update selection, state transitions, cancellation, and duplicate
  suppression live in
  `WindowsCompanion.Core`.
- HTTP, assembly-version discovery, diagnostics, Windows toast creation, release
  link launching, and tray presentation live in `WindowsCompanion.App`.
- With no known newer release, the tray offers **Check for updates…**. It first
  opens, restores, and focuses the main window, immediately shows **Checking for
  updates…**, and then presents the current, available, or nonfatal error result.
- With a known newer release, the tray offers **Install update…**. It opens and
  focuses the app and shows the existing available-update surface. Whether this
  triggers a background download, or only opens the release page, depends on the
  update mode described in `specs/015-auto-update-install/spec.md`.
- The toast names the installed and available versions and offers **View release**,
  which opens the exact trusted `html_url` returned for that release.
- While an update is available, the notification-area icon uses the update variant:
  its companion sphere is larger, red, and contains a white `1`. The tooltip and
  tray menu repeat the available version, so the state is never conveyed by colour
  alone.
- The top in-app banner presents checking, current, available, and clear check
  failure states. A known update exposes **View release** as its primary action,
  and every completed result exposes **Recheck for updates**.
- Browser launch failures replace the banner message with a clear retryable error
  without clearing the known release or affecting Home Assistant connectivity.
- Single-clicking or double-clicking the tray icon and the tray's show command
  share one dispatcher-owned activation path. It idempotently handles hidden,
  minimized, and already-visible windows, and explicitly foregrounds the native
  window after showing or restoring it. H.NotifyIcon.WinUI is wired through
  `LeftClickCommand`, `DoubleClickCommand`, and command-bound flyout items, not
  ignored XAML click handlers.
- The badge remains while the newer release is known, including after a failed
  recheck. Opening its page does not claim that it was downloaded or installed.
- Only release artifacts built by `scripts/build-release.ps1` opt into update
  checks. Source builds, pull-request builds, and ordinary CI artifacts skip the
  request because their `version.txt` value is not a truthful statement that they
  are the corresponding shipped release.
- Automatic failures are diagnostic-only. User-initiated network and parsing
  failures are also shown in-app, and no failure contains a Home Assistant URL,
  credential, network identifier, or sensor data.

## Version and release contract

- `version.txt` supplies the assembly informational version.
- The release workflow accepts `v<semver>` tags and passes `OfficialBuild=true`
  while publishing each architecture.
- A leading `v` or `V` is accepted for release tags. SemVer build metadata is
  ignored for precedence; prerelease identifiers use SemVer numeric and lexical
  ordering.
- Release pages must use HTTPS on `github.com` under
  `/DevSecNinja/home-assistant-win-companion/releases/tag/`.
