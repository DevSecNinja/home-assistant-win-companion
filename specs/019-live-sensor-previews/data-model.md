# Data Model: Live Sensor Previews

## Sensor Preview

- **Identity**: Stable sensor unique ID.
- **Displayed value**: Latest successfully returned preview text, or the existing disabled/unavailable text.
- **Relationships**: One preview text element corresponds to one catalog definition.
- **Validation**: Values may update only when the displayed control still belongs to the active catalog and refresh session.

## Refresh Session

- **State**: `Stopped`, `Waiting`, or `Reading`.
- **Active conditions**: Sensors view selected, window visible, presenter not minimized, application not shutting down.
- **Ownership**: At most one session belongs to the Main window.
- **Transitions**:
  - `Stopped -> Waiting`: Sensors view becomes actively presented.
  - `Waiting -> Reading`: Immediate or scheduled refresh begins.
  - `Reading -> Waiting`: Refresh completes while active conditions still hold.
  - `Waiting/Reading -> Stopped`: View/window presentation ends or shutdown begins; active read is cancelled.
