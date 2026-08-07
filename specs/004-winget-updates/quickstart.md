# Quickstart: WinGet Update Status

## Build and test

```powershell
.\scripts\run.ps1
dotnet test tests\HaCompanion.Core.Tests\HaCompanion.Core.Tests.csproj
```

## First enablement

1. Remove or rename the current-user `Microsoft.WinGet.Client` module for the test.
2. Open **Sensors...** and switch on **WinGet Updates**.
3. Confirm the dialog explains the official module, PowerShell Gallery, current-user
   scope, and approximate size.
4. Cancel and verify the toggle remains off.
5. Try again, accept installation, and verify the UI remains responsive.
6. Confirm the sensor becomes enabled only after installation succeeds.

## Update count and privacy

1. Compare the sensor count with:

   ```powershell
   Get-WinGetPackage | Where-Object IsUpdateAvailable
   ```

2. Open Sensors and verify local package/version details.
3. Inspect the Home Assistant entity payload and app log; confirm no package details
   appear.

## Scheduling

1. Trigger several normal sensor syncs and confirm they use the cached count.
2. Select **Update now** and confirm one fresh PowerShell check completes before the
   push.
3. Disable the sensor and verify no further PowerShell process is created.
