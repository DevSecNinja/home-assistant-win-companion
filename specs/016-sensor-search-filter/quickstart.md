# Quickstart: Sensor Search Filter

## Prerequisites

- Windows 10 build 19041+ or Windows 11
- .NET 10 SDK installed
- Windows App Runtime 2.3

## Build & Launch

```powershell
.\scripts\run.ps1
```

## Validation Scenarios

### Scenario 1: Basic filtering

1. Connect to a Home Assistant instance (or use demo mode)
2. Open the sensors page (click sensor count on status, or Settings → Choose Sensors)
3. Observe the search box at the top of the sensor list
4. Type "wifi" into the search box
5. **Expected**: Only sensors with "wifi" in their name are visible

### Scenario 2: Case-insensitive match

1. Type "BATTERY" into the search box
2. **Expected**: Sensors with "battery" or "Battery" in their name are shown

### Scenario 3: Clear filter

1. With an active filter, click the X button in the search box
2. **Expected**: All sensors reappear

### Scenario 4: No results

1. Type "zzzznonexistent" into the search box
2. **Expected**: No sensor cards shown; an empty-state message like "No sensors match your search" appears

### Scenario 5: Navigation reset

1. With an active filter, press the Back button to leave the sensors page
2. Re-open the sensors page
3. **Expected**: Search box is empty and all sensors are visible
