# Tasks: Startup Update Checks

> **Retroactive reconstruction.** Tasks derived from shipped code, not generated
> before implementation. All marked complete.

- [x] T001 Add SemanticVersion parser with prerelease/build-metadata handling and precedence comparison.
- [x] T002 Add ReleaseCatalog state machine (idle, checking, current, available, error) with single-flight and cancellation.
- [x] T003 Implement StartupUpdateService with GitHub Releases REST call, timeout, and source-build guard.
- [x] T004 Add tray menu actions: "Check for updates…" (no release known) and "Install update…" (release known).
- [x] T005 Add Windows toast with installed/available versions and "View release" action with trusted URL validation.
- [x] T006 Add top in-app banner presenting check states with "View release" and "Recheck for updates" actions.
- [x] T007 Implement update-badge tray icon variant with tooltip and accessibility text.
- [x] T008 Wire tray/double-click activation to shared dispatcher-owned path with idempotent window show/focus.
- [x] T009 Add unit tests for SemanticVersion parsing, release filtering, state transitions, and duplicate suppression.
