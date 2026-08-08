# Research: WinGet Update Status

## Decision: Use Microsoft.WinGet.Client through Windows PowerShell

The official module returns structured package objects with
`IsUpdateAvailable`, avoiding localized CLI output. It supports Windows PowerShell
5.1 and PowerShell 7, so the app invokes the built-in Windows PowerShell executable
and requests compact JSON containing only the fields required for local display.

Alternatives considered:

- WinGet COM API: Microsoft does not publish a consumable .NET projection package;
  integration requires vendoring metadata and maintaining a custom CsWinRT
  projection.
- `winget upgrade` parsing: current WinGet has no JSON output for this command and
  its table is localized.

## Decision: Require explicit user installation

The module is not installed with WinGet. First enablement checks for a sufficiently
recent Microsoft-signed module and provides a copyable PowerShell Gallery command
when it is absent. The app does not download, install, or update executable code.
The preference remains disabled until the user completes setup explicitly.

## Decision: Cache checks for six hours

The source starts one asynchronous check after enablement and refreshes every six
hours. Normal one-minute sensor syncs use the cache. Update now calls a new
refreshable-source hook before pushing enabled sensors.

## Decision: Keep package details local

PowerShell emits name, id, installed version and newest available version. Core
stores these only in memory. The Home Assistant reading is an integer count or
`unavailable`; local preview formats package names and versions without placing
them in attributes or logs.

## Decision: Explicit result states

Results distinguish ready, checking, module missing, timeout, command failure and
malformed output. Only ready has a numeric count. Errors cannot fail the overall
sensor batch.
