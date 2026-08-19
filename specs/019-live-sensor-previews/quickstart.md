# Quickstart: Validate Live Sensor Previews

## Prerequisites

- Windows with the repository's .NET 10 and Windows App SDK prerequisites.
- An enabled sensor whose value can be changed, such as Now Playing.

## Automated validation

```powershell
dotnet test --project tests\WindowsCompanion.App.Tests\WindowsCompanion.App.Tests.csproj -c Release -- --filter-query "/*/*/*SensorPreview*/*"
```

Run any targeted UI scenario added for the Sensors page through the repository's existing UI test command.

## Manual validation

1. Launch with `.\scripts\run.ps1`.
2. Open **Sensors**, enable **Now Playing**, and start or change media.
3. Confirm **Current value** changes within five seconds without navigating away.
4. Enter a search term and confirm refresh does not clear the filter or recreate controls.
5. Minimize for at least five seconds, change media, restore, and confirm a fresh value appears promptly.
6. Close to tray, change media, reopen, and confirm the same behavior.
7. Disable a sensitive sensor and confirm its protected preview remains unchanged.

## Expected outcome

Previews remain current only while the page is actively presented, no duplicate refreshes occur, and privacy-gated sensors are not collected before opt-in.
