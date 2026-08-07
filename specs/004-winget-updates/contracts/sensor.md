# Sensor Contract: WinGet Updates

| Property | Value |
| --- | --- |
| Unique id | `winget_updates` |
| Name | WinGet Updates |
| Type | `sensor` |
| Entity category | `diagnostic` |
| Default | Disabled |
| Successful state | Integer available-update count |
| Failed state | `unavailable` |

No package names, identifiers, versions, error output, or check timestamps are
included in attributes.

## Local preview

- Disabled: `Enable to check for updates`
- Checking: `Checking for updates...`
- Ready with zero: `No updates available`
- Ready with updates: one line per package, `Name: installed -> available`
- Failed: actionable local error without raw process output
