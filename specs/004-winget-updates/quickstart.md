# Quickstart: WinGet Update Status

## Build and test

```powershell
.\scripts\run.ps1
dotnet test --project tests\WindowsCompanion.Core.Tests\WindowsCompanion.Core.Tests.csproj
```

## First enablement

1. Remove or rename the current-user `Microsoft.WinGet.Client` module for the test.
2. Open **Sensors...** and switch on **WinGet Updates**.
3. Confirm the dialog explains the exact capability failure and provides a copyable
   current-user PowerShell Gallery command that explicitly invokes Windows
   PowerShell 5.1.
4. Close the dialog and verify the toggle remains off.
5. Run the command as the same Windows user while leaving the companion open.
6. Select **Recheck** (or enable the sensor again) and confirm the fresh probe accepts
   the Microsoft-signed module without restarting the companion.
7. Confirm that an older module, unavailable Windows PowerShell host, invalid
   signature, import failure, and query failure produce different actionable
   messages.

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
