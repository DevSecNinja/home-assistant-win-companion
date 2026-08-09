# Implementation Plan: Frontmost Application Sensor

Keep mode selection, truncation, and debounce state in Core. Add a Windows
`SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` source in App. Foreground callbacks stage
local values; a four-second timer commits only the final distinct value and never
requests an immediate sync.

Files:

- `src/WindowsCompanion.Core/Sensors/FrontmostAppState.cs`
- `src/WindowsCompanion.Core/Sensors/SensorPreferences.cs`
- `src/WindowsCompanion.App/Services/FrontmostAppSensorSource.cs`
- `src/WindowsCompanion.App/MainWindow.xaml(.cs)`
- `tests/WindowsCompanion.Core.Tests/FrontmostAppTests.cs`
