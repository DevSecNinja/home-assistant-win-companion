# Quickstart: Validate Display and Monitor Search Aliases

## Automated Validation

Run the targeted UI test:

```powershell
.\scripts\test.ps1 -Ui -Filter "/*/*/SensorFilterUiTests/*"
```

The UI scenario requires an unlocked interactive desktop that permits UI
Automation keyboard input.

Run the Core tests:

```powershell
dotnet test --project tests\WindowsCompanion.Core.Tests\WindowsCompanion.Core.Tests.csproj -c Release
```

Build without launching:

```powershell
.\scripts\run.ps1 -NoLaunch
```

## Expected Outcomes

1. The sensor card for `display_identity` is labeled `Display Identity`.
2. Filtering for `display` shows the card.
3. Filtering for `monitor` shows the same card.
4. Filtering remains case-insensitive.
5. Clearing the filter restores the full list.
6. The sensor's unique ID is `display_identity`.
7. Public state and attribute labels use `display`/`displays`.
8. A persisted `monitor_identity` registration is retired as a removed sensor.
