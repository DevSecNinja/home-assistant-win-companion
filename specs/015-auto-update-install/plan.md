# Implementation Plan: Auto-Update Download and Install

**Date**: 2026-08 (retroactive) | **Spec**: [spec.md](./spec.md)

> **Retroactive reconstruction.** This plan was written after the feature shipped,
> based on the spec and the implemented source. It was not generated before
> implementation.

## Summary

Add a user-configurable update mode (auto-install / notify-only / disabled), a
background download-and-verify pipeline, a detached PowerShell installer helper,
and unified presentation across tray, banner and Settings — building on the
existing startup-update-check infrastructure from spec 011.

## Files

- `src/WindowsCompanion.Core/Updates/UpdatePreferences.cs`
- `src/WindowsCompanion.Core/Updates/UpdateAssetSelector.cs`
- `src/WindowsCompanion.Core/Updates/UpdateInstaller.cs`
- `src/WindowsCompanion.App/Services/UpdatePackageDownloader.cs`
- `src/WindowsCompanion.App/Services/UpdatePackageVerifier.cs`
- `src/WindowsCompanion.App/Services/SilentUpdateInstaller.cs`
- `src/WindowsCompanion.App/Services/UpdateStatusPresentation.cs`
- `src/WindowsCompanion.App/Services/UpdateUiActions.cs`
- `src/WindowsCompanion.App/AppController.UpdatesAndDemo.cs`
- `src/WindowsCompanion.App/MainWindow.Updates.cs`
- `tests/WindowsCompanion.Core.Tests/UpdateInstallerTests.cs`
- `tests/WindowsCompanion.Core.Tests/UpdateAssetSelectorTests.cs`
- `tests/WindowsCompanion.Core.Tests/UpdatePackageDownloaderTests.cs`
- `tests/WindowsCompanion.Core.Tests/UpdatePackageVerifierTests.cs`
- `tests/WindowsCompanion.Core.Tests/SilentUpdateInstallerTests.cs`
