# Implementation Plan: Startup Update Checks

**Date**: 2026-08 (retroactive) | **Spec**: [spec.md](./spec.md)

> **Retroactive reconstruction.** This plan was written after the feature shipped,
> based on the spec and the implemented source. It was not generated before
> implementation.

## Summary

Non-blocking release check at process startup and user-initiated recheck, with
shared state across tray, banner and Settings. SemVer parsing, precedence, and
state transitions in Core; HTTP, toast, and tray presentation in App.

## Files

- `src/WindowsCompanion.Core/Updates/ReleaseCatalog.cs`
- `src/WindowsCompanion.Core/Updates/SemanticVersion.cs`
- `src/WindowsCompanion.App/Services/StartupUpdateService.cs`
- `src/WindowsCompanion.App/Services/UpdateStatusPresentation.cs`
- `src/WindowsCompanion.App/MainWindow.Updates.cs`
- `tests/WindowsCompanion.Core.Tests/StartupUpdateTests.cs`
- `tests/WindowsCompanion.Core.Tests/GitHubReleaseClientTests.cs`
