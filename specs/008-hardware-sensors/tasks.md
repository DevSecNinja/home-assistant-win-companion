# Tasks: Hardware, Display, Environment and Storage Sensors

> **Retroactive reconstruction.** Tasks derived from shipped code, not generated
> before implementation. All marked complete.

- [x] T001 Add HardwareInfo source with host_model from SMBIOS registry, filtering OEM placeholders.
- [x] T002 Add DisplayTopology Core model with resolution formatting, type classification, and bounded output.
- [x] T003 Add DisplaySensorSource using EnumDisplayMonitors/EnumDisplaySettings/CCD with DisplaySettingsChanged hook.
- [x] T004 Add WindowsTheme Core model and sensor source with UserPreferenceChanged push and high-contrast attributes.
- [x] T005 Add LocaleFormatter in Core and LocaleSensorSource reading live regional format with language/region attributes.
- [x] T006 Add DiskUsage Core model with percentage-point/GB change gates and 10-minute poll cadence.
- [x] T007 Add DiskUsageSensorSource for system drive with unavailable fallback for inaccessible volumes.
- [x] T008 Add PendingReboot Core model checking WU/CBS registry keys and PendingFileRenameOperations.
- [x] T009 Add PendingRebootSensorSource with 10-minute poll and flip-only push.
- [x] T010 Register all sources in SensorCatalog, add unit tests, and validate build.
