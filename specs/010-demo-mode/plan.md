# Implementation Plan: Demo Mode

**Date**: 2026-08 (retroactive) | **Spec**: [spec.md](./spec.md)

> **Retroactive reconstruction.** This plan was written after the feature shipped,
> based on the spec and the implemented source. It was not generated before
> implementation.

## Summary

Allow users to explore the full sensor catalog with live local previews before
connecting to Home Assistant. Keep demo-session state in Core; wire entry/exit in
AppController alongside the existing update-and-demo partial class.

## Files

- `src/WindowsCompanion.Core/App/DemoSession.cs`
- `src/WindowsCompanion.App/AppController.UpdatesAndDemo.cs`
- `src/WindowsCompanion.App/MainWindow.xaml(.cs)` (demo banner and action hiding)
- `tests/WindowsCompanion.Core.Tests/DemoSessionTests.cs`
