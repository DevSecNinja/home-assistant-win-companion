# Feature Specification: Demo Mode

**Status**: Shipped

**Input**: Issue #107, "add demo mode".

Someone evaluating the companion can see the whole sensor catalog, with each
sensor's current value read on their own PC, before they have a Home Assistant
server to point it at.

## Requirements

- The sign-in screen offers "Explore in demo mode". It needs no URL, no browser
  round-trip and no credentials.
- The demo never contacts Home Assistant: no OAuth, no device registration, no
  webhook, no WebSocket and no sensor push.
- The demo writes nothing. The server settings file is untouched, no secret is
  stored, and the sensor choices made in the demo are discarded when it ends.
- No sensor source is started. Values come from the same local preview the
  Sensors screen already uses, which reads once per request and still applies
  `SensorPreviewGate`, so a privacy-sensitive value is only read after the user
  switches that sensor on. Nothing shown locally is transmitted.
- A warning banner naming demo mode is visible on every screen for as long as the
  demo runs, and carries the action that leaves it.
- Actions that only make sense against a server (Open Home Assistant, Connection,
  Update now, Disconnect, Remove server) are hidden during the demo, and the
  status view says there is no server and that nothing has been sent.
- Signing in or resuming a saved session ends the demo, so its catalog can never
  shadow the one built for the live connection.
