# Feature Specification: Startup Update Checks

**Status**: Shipped

**Input**: Issue #116, "Add startup update checks".

Released builds make one best-effort request at process startup and tell the user
when a newer stable GitHub Release is available. The companion never downloads,
installs or restarts itself.

## Requirements

- The check starts only after Windows app notifications are registered and is not
  awaited by window creation, saved-session resume, or Home Assistant connection.
- One process performs at most one request and shows at most one update
  notification. A timeout, cancellation, malformed response, HTTP failure, draft,
  prerelease, invalid version, or untrusted release URL produces no notification.
- The source is GitHub's public Releases REST endpoint for
  `DevSecNinja/home-assistant-win-companion`. The request uses HTTPS, identifies the
  product, asks for GitHub JSON, pins an API version, rejects redirects, and has a
  five-second timeout.
- SemVer parsing, precedence, draft/prerelease filtering, trusted release-page
  validation, update selection, and duplicate suppression live in
  `WindowsCompanion.Core`.
- HTTP, assembly-version discovery, diagnostics, Windows toast creation, release
  link launching, and tray presentation live in `WindowsCompanion.App`.
- The toast names the installed and available versions and offers **View release**,
  which opens the exact trusted `html_url` returned for that release.
- While an update is available, the notification-area icon uses the update variant:
  its companion sphere is larger, red, and contains a white `1`. The tooltip and
  tray menu repeat the available version, so the state is never conveyed by colour
  alone.
- The open window uses a persistent informational banner with a short, wrapping
  version message and the same **View release** action. It stays usable at the
  minimum window width even when the product heading wraps after "Home".
- The badge remains for the process run. Opening the release page does not claim
  that the update was downloaded or installed.
- Only release artifacts built by `scripts/build-release.ps1` opt into update
  checks. Source builds, pull-request builds, and ordinary CI artifacts skip the
  request because their `version.txt` value is not a truthful statement that they
  are the corresponding shipped release.
- Failures are diagnostic-only and contain no Home Assistant URL, credentials,
  network identifier, or sensor data.

## Version and release contract

- `version.txt` supplies the assembly informational version.
- The release workflow accepts `v<semver>` tags and passes `OfficialBuild=true`
  while publishing each architecture.
- A leading `v` or `V` is accepted for release tags. SemVer build metadata is
  ignored for precedence; prerelease identifiers use SemVer numeric and lexical
  ordering.
- Release pages must use HTTPS on `github.com` under
  `/DevSecNinja/home-assistant-win-companion/releases/tag/`.
