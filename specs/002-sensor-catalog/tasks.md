# Tasks: Selectable Sensor Catalog

> **Retroactive reconstruction.** Tasks derived from shipped code, not generated
> before implementation. All marked complete.

- [x] T001 Define SensorDefinition with stable id, name, type, device class, icon, description, privacy, and default state.
- [x] T002 Add SensorPreferences persistence in non-secret local config with per-sensor enable/disable and idle threshold.
- [x] T003 Implement SensorCatalog orchestrating source start/stop based on enabled sensors and string truncation to 255 chars.
- [x] T004 Add ISensorSource contract with Start/Stop lifecycle, onChanged callback, and requested-IDs filtering.
- [x] T005 Add ActiveSensorSource with idle, lock, screensaver, sleep, and fast-user-switch sub-states and configurable idle threshold.
- [x] T006 Add NetworkSensorSource for Connection Type, IP Address with event-driven push on network changes.
- [x] T007 Add SystemSensorSource for OS Version and Last Boot.
- [x] T008 Wire Sensors page in main window with per-sensor toggles, descriptions, and privacy labels.
- [x] T009 Implement register_sensor for enable/disable so HA entities reflect user choices.
- [x] T010 Add status view with last successful push time, health verdict, rolling log, and update-now action.
- [x] T011 Add unit tests for catalog lifecycle, preference persistence, and push deduplication.
