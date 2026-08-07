# Implementation Plan: Frontmost Application Sensor

Keep mode selection, truncation, and debounce state in Core. Add a Windows
`SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` source in App. Foreground callbacks stage
local values; a four-second timer commits only the final distinct value and never
requests an immediate sync.

Files:

- `src/HaCompanion.Core/Sensors/FrontmostAppState.cs`
- `src/HaCompanion.Core/Sensors/SensorPreferences.cs`
- `src/HaCompanion.App/Services/FrontmostAppSensorSource.cs`
- `src/HaCompanion.App/MainWindow.xaml(.cs)`
- `tests/HaCompanion.Core.Tests/FrontmostAppTests.cs`
