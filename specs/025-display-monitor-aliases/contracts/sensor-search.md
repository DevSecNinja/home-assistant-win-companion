# Contract: Sensor Catalog Search

## Visible Metadata

The sensor with unique ID `display_identity` is presented by current application
metadata as:

- Name: `Display Identity`
- Search aliases: `Monitor`, `Monitors`

The retired predecessor ID is `monitor_identity`.

## Search Behavior

Sensor catalog filtering:

1. Trims leading and trailing whitespace from the user's query.
2. Treats an empty result as no filter.
3. Matches case-insensitive substrings against the card's searchable metadata.
4. Includes configured search aliases in searchable metadata.
5. Shows the no-results state only when a non-empty query matches no cards.

## Data Boundary

Search aliases are local catalog metadata. They are not included in sensor state,
attributes, webhook payloads, persisted preferences, or logs.

## Home Assistant Payload Terminology

- Zero-display state: `No displays`
- Multiple-display state: `{count} displays`
- Unknown fallback: `Unknown display`
- Structured collection attribute: `displays`
