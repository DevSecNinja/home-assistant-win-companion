# Research: Live Sensor Previews

## Decision: Refresh preview text in place

- **Rationale**: Rebuilding the list would recreate toggles and metadata, disrupt focus/search state, and risk firing control events. Updating the existing text map preserves the user's interaction.
- **Alternatives considered**: Rebuild the full Sensors page every interval; add a manual refresh button.

## Decision: Use a two-second, single-flight refresh

- **Rationale**: It matches the Now Playing source's existing collection cadence and keeps visible changes comfortably inside the five-second requirement. Skipping ticks while a read is active prevents overlap.
- **Alternatives considered**: Event-only refresh, which is not consistently available across sources; sub-second polling, which adds unnecessary work; a five-second interval, which leaves no timing margin.

## Decision: Gate refresh by view and window presentation

- **Rationale**: The page should stop causing work when another view is selected, the window is hidden to tray, or its presenter is minimized. Window presentation changes are the authoritative native lifecycle signal.
- **Alternatives considered**: Run continuously once the page is first opened; gate only on XAML visibility.

## Decision: Use cached enabled readings for periodic updates

- **Rationale**: The initial full preview remains privacy-gated, while periodic updates read only enabled sensors from source-owned snapshots. This preserves expensive source polling cadences and avoids probing disabled benign sources every two seconds.
- **Alternatives considered**: Re-run every source's active preview operation, read individual sources from the UI, or duplicate privacy checks in the page.
