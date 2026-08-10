# Contract: Native UI Automation

## Stable element identity

Every control used by automation has a unique, semantic
`AutomationProperties.AutomationId`. IDs are stable contracts and do not contain
localized display text or positional indices.

Initial required IDs:

- `Connect.Url`
- `Connect.SignIn`
- `Connect.Error`
- `Status.Server`
- `Status.Connection`
- `Status.Health`
- `Status.UpdateNow`
- `Status.Disconnect`
- `Status.OpenSensors`
- `Status.RemoveServer`
- `Sensors.List`
- `Sensors.Save`
- `Dialog.Primary`
- `Dialog.Cancel`

Dynamic sensor controls derive IDs from stable sensor IDs, for example
`Sensors.Toggle.battery_level`.

## Interaction rules

- Page objects locate by automation ID and expected control type.
- Tests use state- or interaction-based waits, never fixed short sleeps.
- Each test launches exactly one app process with one isolated profile.
- Tests restore the main window before interaction and do not depend on screen
  coordinates, theme, DPI, or window position.
- Modal dialogs are found within the owning window and explicitly dismissed.
- Tray-icon and native-toast rendering are separate environment-gated scenarios;
  their absence does not weaken main-window coverage.

## Required smoke journeys

1. Clean launch shows Connect and validates an empty URL.
2. Sign-in against the fake server reaches connected Status.
3. Sensor settings change and produce the expected fake-server interaction.
4. Disconnect and reconnect update both UI and server-observed connection state.
5. Remove server clears the isolated profile and returns to Connect.
6. Configured auth/connectivity failure displays an actionable error and permits
   retry.
7. On runners that expose an interactive shell and notification capability, a
   local push produces a native notification and the hidden window is restored
   through the tray affordance.

## Failure evidence

On failure the fixture captures:

- current window screenshot
- sanitized UI tree summary containing IDs, control types, names, enabled state,
  and visibility
- isolated app log
- sanitized fake-server interactions
- TRX result

Captured UI text is treated as potentially sensitive and redacted through the
same fixture policy as application logs.

If the runner lacks notification or shell capability, the environment probe must
report the exact unsupported capability; it must not report the smoke scenario as
passing.
