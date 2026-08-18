# Tasks: Auto-Update Download and Install

> **Retroactive reconstruction.** Tasks derived from shipped code, not generated
> before implementation. All marked complete.

- [x] T001 Add UpdatePreferences model with three-mode enum and persistence in settings.json.
- [x] T002 Add UpdateAssetSelector to resolve architecture-matching setup ZIP and checksum sidecar from a GitHub release.
- [x] T003 Implement UpdatePackageDownloader with progress reporting and cancellation-safe streaming to partial file.
- [x] T004 Implement UpdatePackageVerifier with SHA-256 checksum and build-provenance attestation validation.
- [x] T005 Add SilentUpdateInstaller: ZIP extraction, PowerShell helper script generation, and detached launch.
- [x] T006 Add UpdateInstaller orchestrator in Core with single-flight semaphore, supersession logic, and download/verify/install transitions.
- [x] T007 Add UpdateStatusPresentation unifying release-check and install states for UI surfaces.
- [x] T008 Wire UpdateUiActions to tray menu, top banner, and Settings page with Install now / View release actions.
- [x] T009 Handle post-install result: read last-install.json on startup and show success/failure banner.
- [x] T010 Add unit tests for asset selection, preference persistence, and installer state transitions.
