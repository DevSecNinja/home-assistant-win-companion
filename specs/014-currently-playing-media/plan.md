# Implementation Plan: Currently Playing Media

**Date**: 2026-08 (retroactive) | **Spec**: [spec.md](./spec.md)

> **Retroactive reconstruction.** This plan was written after the feature shipped,
> based on the spec and the implemented source. It was not generated before
> implementation.

## Summary

Add `media_now_playing` and `media_playing` sensors backed by Windows SMTC. Keep
playback-state classification, session preference logic, and attribute formatting
in Core. Keep the WinRT session-manager subscription and AUMID resolution in the
App sensor source. Poll on a 2-second cadence with change-driven push.

## Files

- `src/WindowsCompanion.Core/Sensors/MediaPlaybackState.cs`
- `src/WindowsCompanion.App/Services/MediaSensorSource.cs`
- `tests/WindowsCompanion.Core.Tests/MediaSensorTests.cs`
- `tests/WindowsCompanion.Core.Tests/MediaSensorSourceTests.cs`
