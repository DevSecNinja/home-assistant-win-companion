# Feature Specification: Capability Usage Sensors (Camera/Microphone)

**Status**: Shipped

Add opt-in `microphone` and `camera` binary sensors that report whether any
application is currently using the respective hardware capability.

## Requirements

- Both sensors default off and are labelled privacy-sensitive.
- `microphone` is on while any app is using a microphone.
- `camera` is on while any app is using a camera.
- Detection reads `LastUsedTimeStop` values from the Windows
  `CapabilityAccessManager\ConsentStore` registry under both HKCU and HKLM. A stop
  value of 0 or negative means the capability is currently in use.
- Activity evaluation logic lives in Core (`CapabilityActivity.IsActive`).
- Polls every second for responsive on-air detection; pushes only on state change
  using `ChangeGate<T>`.
- Uses `SensorPollLoop` for cancellable single-flight polling.
- Registry access failures (unauthorized, security, IO) are silently skipped per
  entry; the sensor does not fail entirely.

## Privacy

- No application names or PIDs are exposed.
- Device names are not logged.
