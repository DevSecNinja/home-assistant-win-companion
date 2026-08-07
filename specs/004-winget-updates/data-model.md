# Data Model: WinGet Update Status

## WinGetPackageUpdate

| Field | Type | Scope |
| --- | --- | --- |
| Name | string | Local preview only |
| Id | string | Local memory only |
| InstalledVersion | string | Local preview only |
| AvailableVersion | string | Local preview only |

## WinGetUpdateResult

| Field | Type | Meaning |
| --- | --- | --- |
| Status | enum | Ready, Checking, ModuleMissing, Timeout, Failed, InvalidOutput |
| Packages | package collection | Updates found during a successful check |
| Error | string? | Local actionable summary; never package output |
| CheckedAt | timestamp? | Time of last completed attempt |

State mapping:

- Ready -> integer package count
- All other completed failure states -> `unavailable`
- Checking with no prior successful result -> `unavailable`
- Checking with a prior successful result -> retain cached count until completion

No update result or package detail is persisted.
